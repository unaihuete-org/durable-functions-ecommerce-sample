﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DurableECommerceWorkflow.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;

namespace DurableECommerceWorkflow.Functions;

public static class OrchestratorFunctions
{
    [FunctionName("O_ProcessOrder")]
    public static async Task<OrderResult> ProcessOrder(
        [OrchestrationTrigger] IDurableOrchestrationContext ctx,
        ILogger log)
    {
        var order = ctx.GetInput<Order>();
        order.OrchestrationId = ctx.InstanceId;

        if (!ctx.IsReplaying)
            log.LogInformation($"Processing order #{order.Id}");

        var total = order.Items.Sum(i => i.Amount);

        await ctx.CallActivityAsync("A_SaveOrderToDatabase", order);

        if (total > 1000)
        {
            if (!ctx.IsReplaying)
                log.LogWarning($"Need approval for {ctx.InstanceId}");

            ctx.SetCustomStatus("Needs approval");
            await ctx.CallActivityAsync("A_RequestOrderApproval", order);

            var approvalResult = await ctx.WaitForExternalEvent<string>("OrderApprovalResult", TimeSpan.FromSeconds(180), null);
            ctx.SetCustomStatus(""); // clear the needs approval flag

            if (approvalResult != "Approved")
            {
                // timed out or got a rejected
                if (!ctx.IsReplaying)
                    log.LogWarning($"Not approved [{approvalResult}]");
                await ctx.CallActivityAsync("A_SendNotApprovedEmail", order);
                return new OrderResult { Status = "NotApproved" };
            }

        }

        string[] downloads = null;
        try
        {
            // create files in parallel
            var tasks = new List<Task<string>>();
            foreach (var item in order.Items)
            {
                tasks.Add(ctx.CallActivityAsync<string>("A_CreatePersonalizedPdf", (order, item)));
            }

            downloads = await Task.WhenAll(tasks);
            //DELETE WHEN SENDGRID WORKING
//             return new OrderResult { Status = "Success", Downloads = downloads };

        }
        catch (Exception ex)
        {
            if (!ctx.IsReplaying)
                log.LogError($"Failed to create files", ex);

            //DELETE WHEN SENDGRID WORKING
            return new OrderResult { Status = "Problem" };
        }

        if (downloads != null)
        {
            await ctx.CallActivityWithRetryAsync("A_SendOrderConfirmationEmail",
                new RetryOptions(TimeSpan.FromSeconds(30), 3),
                (order, downloads));
            return new OrderResult { Status = "Success", Downloads = downloads };
        }
        await ctx.CallActivityWithRetryAsync("A_SendProblemEmail",
            new RetryOptions(TimeSpan.FromSeconds(30), 3),
            order);
        return new OrderResult { Status = "Problem" };

        

    }

}
