﻿using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using System;
using System.Threading.Tasks;

namespace DurableECommerceWorkflow
{
    public static class OrchestratorFunctions
    {
        [FunctionName("O_ProcessOrder")]
        public static async Task<object> ProcessOrder(
            [OrchestrationTrigger] DurableOrchestrationContext ctx,
            TraceWriter log)
        {
            var order = ctx.GetInput<Order>();
            order.OrchestrationId = ctx.InstanceId;

            if (!ctx.IsReplaying)
                log.Info($"Processing order for {order.ProductId}");

            await ctx.CallActivityAsync("A_SaveOrderToDatabase", order);

            if (order.Amount > 1000)
            {
                if (!ctx.IsReplaying)
                    log.Warning($"Need approval for {ctx.InstanceId}");

                ctx.SetCustomStatus("Needs approval");
                await ctx.CallActivityAsync("A_RequestOrderApproval", order);

                var approvalResult = await ctx.WaitForExternalEvent<string>("OrderApprovalResult", TimeSpan.FromSeconds(180));
                ctx.SetCustomStatus(""); // clear the needs approval flag

                if (approvalResult == null)
                {
                    // timed out
                    await ctx.CallActivityAsync("A_SendNotApprovedEmail", order);
                    return new { Status = "NotApproved" }; 
                }

            }

            string pdfLocation = null;
            string videoLocation = null;
            try
            {
                // create files in parallel
                var pdfTask = ctx.CallActivityAsync<string>("A_CreatePersonalizedPdf", order);
                var videoTask = ctx.CallActivityAsync<string>("A_CreateWatermarkedVideo", order);
                await Task.WhenAll(pdfTask, videoTask);
                pdfLocation = pdfTask.Result;
                videoLocation = videoTask.Result;
            }
            catch (Exception ex)
            {
                if (!ctx.IsReplaying)
                    log.Error($"Failed to create files", ex);
            }

            if (pdfLocation != null && videoLocation != null)
            {
                await ctx.CallActivityWithRetryAsync("A_SendOrderConfirmationEmail",
                    new RetryOptions(TimeSpan.FromSeconds(30), 3),
                    (order, pdfLocation, videoLocation));
                return new { Status = "Success", Pdf = pdfLocation, Video = videoLocation };
            }
            await ctx.CallActivityWithRetryAsync("A_SendProblemEmail",
                new RetryOptions(TimeSpan.FromSeconds(30), 3),
                order);
            return new { Status = "Problem" };
        }

        [FunctionName("O_ProcessOrder_V3")]
        public static async Task<object> ProcessOrderV3(
            [OrchestrationTrigger] DurableOrchestrationContext ctx,
            TraceWriter log)
        {
            var order = ctx.GetInput<Order>();
            
            if (!ctx.IsReplaying)
                log.Info($"Processing order for {order.ProductId}");

            await ctx.CallActivityAsync("A_SaveOrderToDatabase", order);

            string pdfLocation = null;
            string videoLocation = null;
            try
            {
                // create files in parallel
                var pdfTask = ctx.CallActivityAsync<string>("A_CreatePersonalizedPdf", order);
                var videoTask = ctx.CallActivityAsync<string>("A_CreateWatermarkedVideo", order);
                await Task.WhenAll(pdfTask, videoTask);
                pdfLocation = pdfTask.Result;
                videoLocation = videoTask.Result;
            }
            catch (Exception ex)
            {
                if (!ctx.IsReplaying)
                    log.Error($"Failed to create files",ex);
            }

            if (pdfLocation != null && videoLocation != null)
            {
                await ctx.CallActivityWithRetryAsync("A_SendOrderConfirmationEmail",
                    new RetryOptions(TimeSpan.FromSeconds(30), 3),
                    (order, pdfLocation, videoLocation));
                return "Order processed successfully";
            }
            await ctx.CallActivityWithRetryAsync("A_SendProblemEmail",
                new RetryOptions(TimeSpan.FromSeconds(30), 3),
                order);
            return "There was a problem processing this order";
        }


        [FunctionName("O_ProcessOrder_V2")]
        public static async Task<object> ProcessOrderV2(
            [OrchestrationTrigger] DurableOrchestrationContext ctx,
            TraceWriter log)
        {
            var order = ctx.GetInput<Order>();

            if (!ctx.IsReplaying)
                log.Info($"Processing order for {order.ProductId}");

            await ctx.CallActivityAsync("A_SaveOrderToDatabase", order);

            // create files in parallel
            var pdfTask = ctx.CallActivityAsync<string>("A_CreatePersonalizedPdf", order);
            var videoTask = ctx.CallActivityAsync<string>("A_CreateWatermarkedVideo", order);
            await Task.WhenAll(pdfTask, videoTask);

            var pdfLocation = pdfTask.Result;
            var videoLocation = videoTask.Result;

            await ctx.CallActivityAsync("A_SendOrderConfirmationEmail", (order, pdfLocation, videoLocation));

            return "Order processed successfully";
        }

        // basic orchestrator, calls all functions in sequence
        [FunctionName("O_ProcessOrder_V1")]
        public static async Task<object> ProcessOrderV1(
            [OrchestrationTrigger] DurableOrchestrationContext ctx,
            TraceWriter log)
        {
            var order = ctx.GetInput<Order>();

            if (!ctx.IsReplaying)
                log.Info($"Processing order for {order.ProductId}");

            await ctx.CallActivityAsync("A_SaveOrderToDatabase", order);
            var pdfLocation = await ctx.CallActivityAsync<string>("A_CreatePersonalizedPdf", order);
            var videoLocation = await ctx.CallActivityAsync<string>("A_CreateWatermarkedVideo", order);
            await ctx.CallActivityAsync("A_SendOrderConfirmationEmail", (order, pdfLocation, videoLocation));

            return "Order processed successfully";
        }
    }
}