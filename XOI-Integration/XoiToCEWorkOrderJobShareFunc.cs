using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using XOI_Integration.DataFactory;
using XOI_Integration.DataFactory.BaseObject;
using XOI_Integration.DataverseRepository;
using XOI_Integration.DataverseRepository.Operations;
using XOI_Integration.DataverseRepository.Provider;
using XOI_Integration.Helper;

namespace XOI_Integration
{
    public class XoiToCEWorkOrderJobShareFunc
    {
        [FunctionName("XoiToCEWorkOrderJobShare")]
        public async Task RunAsync(
            [ServiceBusTrigger("xoitoceworkorderjobshare", Connection = "SBConnection")] string myQueueItem,
            ILogger log)
        {
            var executionId = Guid.NewGuid();   // Correlation ID
            Guid bookableResourceBookingId = Guid.Empty;

            try
            {
                // Init Dataverse
                log.LogInformation("Execution started. ExecutionId={ExecutionId}", executionId);
                DataverseApi.Initialize(Environment.GetEnvironmentVariable("DataverseConnectionString", EnvironmentVariableTarget.Process));

                // Extract Booking Id from SB payload
                bookableResourceBookingId = DeserializeJSON.GetBookableResourceBookingId(myQueueItem);
                //log Execution ID
                log.LogInformation("Processing BookingId={BookingId} ExecutionId={ExecutionId}",
            bookableResourceBookingId, executionId);

                // Check if WO already has another resource with XOi job details (copy scenario)
                (bool hasOtherResources, Guid copyFromBookableresourcebookingid) =
                    await BookableResourceChecker.CheckForOtherResourcesAndJobIdAsync(bookableResourceBookingId);

                //log
                log.LogInformation(
          "HasOtherResources={HasOtherResources}, CopyFromBookingId={CopyFromBookingId}, ExecutionId={ExecutionId}",
          hasOtherResources,
          copyFromBookableresourcebookingid,
          executionId);

                // Keep your existing log, but make it accurate
                if (hasOtherResources && copyFromBookableresourcebookingid != Guid.Empty)
                {
                    log.LogInformation(
                        "A job has already been created for the corresponding WorkOrder/Project. CopyFrom Booking ID: {CopyFromBookingId}",
                        copyFromBookableresourcebookingid);
                }
                else
                {
                    log.LogInformation("No existing job found to copy for this WorkOrder/Project.");
                }

                // Load job data for XOi
                JobRelatedData jobRelatedData = await JobRelatedDataFactory.CreateAsync(bookableResourceBookingId);
                await jobRelatedData.LoadData();
                log.LogInformation("Job data loaded for BookingId={BookingId} ExecutionId={ExecutionId}", bookableResourceBookingId, executionId);

                // Determine whether to Create or Update
                var xOiJobId = await BookableResourceBookingOperation.GetXOiJobIdAsync(bookableResourceBookingId);
                var operationType = XOiOperationType.DetermineOperationType(myQueueItem, xOiJobId);
                log.LogInformation("OperationType={OperationType} ExecutionId={ExecutionId}", operationType, executionId);

                // Execute operation
                var xOiToBookableResourceDataHandler = new XOiToBookableResourceDataHandler(log);
                var xOiToBookableResourceData =
                    await xOiToBookableResourceDataHandler.HandleXOiToBookableResourceDataAsync(
                        operationType,
                        jobRelatedData,
                        bookableResourceBookingId,
                        xOiJobId
                    );
                log.LogInformation("Integration handled for BookingId={BookingId} ExecutionId={ExecutionId}", bookableResourceBookingId, executionId);
                // Integration log
                log.LogInformation("Create integration logs");
                await IntegrationLogOperation.CreateLogAsync(bookableResourceBookingId, xOiToBookableResourceData);

                log.LogInformation("XoiToCEWorkOrderJobShare function completed");
            }
            catch (Exception ex)
            {
                // IMPORTANT: Throw Service Bus retries (DeliveryCount increments / DLQ works)
                log.LogError(ex, "XoiToCEWorkOrderJobShare FAILED. BookingId={BookingId}", bookableResourceBookingId);
                throw;
            }
        }
    }
}