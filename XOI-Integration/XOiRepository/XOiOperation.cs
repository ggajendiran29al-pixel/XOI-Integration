using Google.Protobuf.WellKnownTypes;
using GraphQL;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XOI_Integration.DataFactory.BaseObject;
using XOI_Integration.DataModels.Enums;
using XOI_Integration.DataverseRepository.Operations;
using XOI_Integration.XOiRepository.Helper;
using XOI_Integration.XOiRepository.Provider;
using XOI_Integration.XOiRepository.XOiDataModels;

namespace XOI_Integration.XOiRepository
{
    public class XOiOperation
    {
        private static readonly Dictionary<OperationType, string> requests = new Dictionary<OperationType, string>()
        {
            {OperationType.Create, @"mutation CreateJob(
              $assigneeIds: [ID!]!,
              $customerName: String!,
              $jobLocation: String!,
              $workOrderNumber: String!,
              $label: String,
              $tags: [String!],
              $tagSuggestions: [String!],
              $internalNoteText: String!
            ) {
              createJob(
                input: {
                  newJob: {
                    assigneeIds: $assigneeIds
                    customerName: $customerName
                    jobLocation: $jobLocation
                    workOrderNumber: $workOrderNumber
                    label: $label
                    tags: $tags
                    tagSuggestions: $tagSuggestions
                    internalNote: { text: $internalNoteText }
                  }
                  additionalActions: { createPublicShare: { enabled: true } }
                }
              ) {
                job {
                  id
                  createdAt
                  createdBy
                  assigneeIds
                  customerName
                  jobLocation
                  workOrderNumber
                  label
                  tags
                  tagSuggestions
                  deepLinks {
                     visionWeb {
                      viewJob {
                        url
                      }
                    }
                    visionMobile {
                      editJob {
                        url
                      }
                      jobLocationActivitySearch {
                        url
                      }
                    }
                  }
                }
                additionalActionsResults {
                  createPublicShare {
                    shareLink
                  }
                }
              }
            }" },
            //update13th feb
            {OperationType.Update, @"mutation UpdateJob(
              $id: ID!,
              $customerName: String!,
              $jobLocation: String!,
              $workOrderNumber: String!,
              $label: String,
              $tags: [String!],
              $tagSuggestions: [String!],
              $internalNoteText: String!,
              $assigneeIds: [ID!]!
            ) {
              updateJob(
                input: {
                  id: $id
                  fieldUpdates: {
                    customerName: $customerName
                    jobLocation: $jobLocation
                    workOrderNumber: $workOrderNumber
                    label: $label
                    tags: $tags
                    tagSuggestions: $tagSuggestions
                    internalNote: { text: $internalNoteText }
                    assigneeIds: $assigneeIds
                  }
                  additionalActions: { createPublicShare: { enabled: true } }
                }
              ) {
                job {
                  id
                  createdAt
                  createdBy
                  assigneeIds
                  customerName
                  jobLocation
                  workOrderNumber
                  label
                  tags
                  tagSuggestions
                  internalNote { text }
                  deepLinks {
                    visionWeb { viewJob { url } }
                    visionMobile { viewJob { url } editJob { url } jobLocationActivitySearch { url } }
                  }
                }
                additionalActionsResults {
                  createPublicShare { shareLink }
                }
              }
            }" },

            {OperationType.GetJobSummary, @"query GetJobSummary(
              $id: ID!, $workflowId: ID) {
              getJobSummary(input: { jobId: $id, workflowJobId: $workflowId }) {
                nextToken
                jobSummary {
                  jobId
                  documentation {
                    workflowName
                    traits
                    tags
                    note {
                      text
                    }
                    choice {
                      chosen
                    }
                    derivedData {
                      make
                      model
                      serial
                      transcript
                      manufacture_date
                    }
                    workSummary {
                        summary_text
                    }
                  }
                  assignees {
                    id
                    email
                    given_name
                    family_name
                  }
                }
              }
            }" },
            {OperationType.GetJob, @"query GetJob(
                $id: ID!) {
                getJob(input: { id: $id }) {
                  job {
                    id
                    createdAt
                    createdBy
                    assigneeIds
                    customerName
                    jobLocation
                    workOrderNumber
                    label
                    tags
                    tagSuggestions
                    deepLinks {
                      visionWeb {
                        viewJob {
                          url
                        }
                      }
                      visionMobile {
                        viewJob {
                          url
                        }
                        editJob {
                          url
                        }
                        jobLocationActivitySearch {
                          url
                        }
                      }
                    }
                  }
                }
              }" }
        };

        private Dictionary<string, GraphQLResponse<XOiJobSummaryResponse>> jobSummaryCache = new Dictionary<string, GraphQLResponse<XOiJobSummaryResponse>>();
        private readonly int maxRetryAttempts = 3;
        private readonly int baseRetryDelayMs = 1000;

        private ILogger _log;
        private readonly XOiAPI _xoiAPI;

        public XOiOperation(ILogger log)
        {
            _log = log;
            _xoiAPI = new XOiAPI();
        }

        public async Task<XOiToBookableResourceData> CreateJobAsync(JobRelatedData jobRelatedData)
        {
            _log.LogInformation("Start create job");

            try
            {
                var query = requests[OperationType.Create];
                var variables = GetVariables(jobRelatedData);

                var response = await SendRequestWithRetryAsync<XOiCRUDResponse>(query, variables, OperationType.Create);

                _log.LogInformation("Job created successfully");

                return XOiProcessResponse.BuildXOiToBookableResourceData(_log, OperationType.Create, response);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to create job");
                throw;
            }
        }

        public async Task<XOiToBookableResourceData> UpdateJobAsync(JobRelatedData jobRelatedData, string jobId)
        {
            _log.LogInformation("Start update job for Job ID: {JobId}", jobId);

            try
            {
                var query = requests[OperationType.Update];
                var variables = GetVariables(jobRelatedData, jobId);

                var response = await SendRequestWithRetryAsync<XOiCRUDResponse>(query, variables, OperationType.Update, jobId);

                _log.LogInformation("Job updated successfully for Job ID: {JobId}", jobId);

                // Commented out until IntegrationLogOperation is updated
                /*
                await IntegrationLogOperation.CreateJobUpdateLogAsync(
                    result: JobResponseResult.Success,
                    jobId: jobId);
                */

                return XOiProcessResponse.BuildXOiToBookableResourceData(_log, OperationType.Update, response);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to update job for Job ID: {JobId}", jobId);

                // Commented out until IntegrationLogOperation is updated
                /*
                await IntegrationLogOperation.CreateJobUpdateLogAsync(
                    result: JobResponseResult.Failure,
                    message: ex.Message,
                    jobId: jobId);
                */

                throw;
            }
        }

        public async Task<XOiJobInfo> GetJobAsync(string jobId)
        {
            _log.LogInformation("Get Job info from XOi for Job ID: {JobId}", jobId);

            try
            {
                var query = requests[OperationType.GetJob];
                var variables = new
                {
                    id = jobId
                };

                var response = await SendRequestWithRetryAsync<XOiCRUDResponse>(query, variables, OperationType.GetJob, jobId);

                return XOiProcessResponse.BuildXOiJobInfoData(_log, response);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to get job info for Job ID: {JobId}", jobId);
                throw;
            }
        }

        public async Task<List<XOiToCustomerAssetData>> GetJobSummaryAsync(string jobId, string workflowJobId)
        {
            try
            {
                var response = await GetJobSummaryResponseAsync(jobId, workflowJobId);
                return XOiProcessResponse.BuildXOiToCustomerAssetData(_log, response);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to get job summary for Job ID: {JobId}, Workflow ID: {WorkflowId}", jobId, workflowJobId);
                throw;
            }
        }
        

        public async Task<XOiWorkSummaryToBookableResourceData> GetJobSummaryWorkflowAsync(string jobId, string workflowJobId)
        {
            try
            {
                var response = await GetJobSummaryResponseAsync(jobId, workflowJobId);
                return XOiProcessResponse.BuildXOiWorkSummaryToBookableResourceData(_log, response, workflowJobId);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to get job summary workflow for Job ID: {JobId}, Workflow ID: {WorkflowId}", jobId, workflowJobId);
                throw;
            }
        }

        private async Task<GraphQLResponse<T>> SendRequestWithRetryAsync<T>(
    string query,
    object variables,
    OperationType operationType,
    string jobId = null)
        {
            int delay = baseRetryDelayMs;
            List<Exception> exceptions = new List<Exception>();

            for (int attempt = 1; attempt <= maxRetryAttempts; attempt++)
            {
                try
                {
                    _log.LogInformation("[XOI] {OperationType} attempt {Attempt}/{MaxAttempts} starting. JobId={JobId}",
                        operationType, attempt, maxRetryAttempts, jobId ?? "");

                    var response = await _xoiAPI.SendRequestAsync<T>(query, variables);

                    // 1) GraphQL errors
                    if (response.Errors != null && response.Errors.Any())
                    {
                        var errorMessage = response.Errors.First().Message ?? "Unknown GraphQL error";
                        _log.LogWarning("[XOI] {OperationType} attempt {Attempt} GraphQL error: {Error}",
                            operationType, attempt, errorMessage);

                        if (attempt == maxRetryAttempts)
                            throw new Exception($"GraphQL error after {maxRetryAttempts} attempts: {errorMessage}");

                        await Task.Delay(GetWaitTime(delay, attempt));
                        continue;
                    }

                    // 2) Null data
                    if (response.Data == null)
                    {
                        _log.LogWarning("[XOI] {OperationType} attempt {Attempt} returned null data",
                            operationType, attempt);

                        if (attempt == maxRetryAttempts)
                            throw new Exception($"Null response after {maxRetryAttempts} attempts");

                        await Task.Delay(GetWaitTime(delay, attempt));
                        continue;
                    }

                    // 3) Peak-time issue: share link missing (Create/Update only)
                    // NOTE: this only works when T is XOiCRUDResponse (your create/update/getJob calls use that)
                    if (typeof(T) == typeof(XOiCRUDResponse) && IsShareLinkMissing(operationType, response))
                    {
                        _log.LogWarning("[XOI] {OperationType} attempt {Attempt} succeeded but ShareLink is EMPTY. Retrying...",
                            operationType, attempt);

                        if (attempt == maxRetryAttempts)
                            throw new Exception("ShareLink is empty after retries");

                        await Task.Delay(GetWaitTime(delay, attempt));
                        continue;
                    }

                    // Success
                    _log.LogInformation("[XOI] {OperationType} succeeded on attempt {Attempt}/{MaxAttempts}",
                        operationType, attempt, maxRetryAttempts);

                    return response;
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);

                    _log.LogWarning(ex, "[XOI] {OperationType} attempt {Attempt} failed with exception",
                        operationType, attempt);

                    if (attempt == maxRetryAttempts)
                        break;

                    await Task.Delay(GetWaitTime(delay, attempt));
                }
            }

            throw new AggregateException($"All {maxRetryAttempts} retry attempts failed for {operationType} operation", exceptions);
        }

        private static readonly Random _jitter = new Random();
        private static readonly object _jitterLock = new object();

        private int GetWaitTime(int baseDelay, int attempt)
        {
            int jitter;
            lock (_jitterLock) jitter = _jitter.Next(0, 100);

            // you can keep your existing formula
            return baseDelay * attempt + jitter;
        }

        private static bool IsShareLinkMissing(OperationType op, object responseObj)
        {
            if (op != OperationType.Create && op != OperationType.Update) return false;

            if (responseObj is GraphQLResponse<XOiCRUDResponse> crud)
            {
                string share =
                    op == OperationType.Create
                        ? crud.Data?.CreateJob?.AdditionalActionsResults?.CreatePublicShare?.ShareLink
                        : crud.Data?.UpdateJob?.AdditionalActionsResults?.CreatePublicShare?.ShareLink;

                return string.IsNullOrWhiteSpace(share);
            }

            return false;
        }

        private async Task<GraphQLResponse<XOiJobSummaryResponse>> GetJobSummaryResponseAsync(string jobId, string workflowJobId)
        {
            _log.LogInformation("Start receiving a job summary for Job ID: {JobId}", jobId);

            try
            {
                // Check cache first
                if (jobSummaryCache.ContainsKey(jobId))
                {
                    _log.LogInformation("Job summary found in cache for Job ID: {JobId}", jobId);
                    return jobSummaryCache[jobId];
                }

                var query = requests[OperationType.GetJobSummary];
                var variables = new
                {
                    id = jobId,
                    workflowId = workflowJobId
                };

                var response = await SendRequestWithRetryAsync<XOiJobSummaryResponse>(
                    query,
                    variables,
                    OperationType.GetJobSummary,
                    jobId);

                if (response.Data != null)
                {
                    _log.LogInformation("Job summary successfully received for Job ID: {JobId}", jobId);

                    // Add to cache
                    jobSummaryCache.Add(jobId, response);

                    // Keep the existing IntegrationLogOperation call since it exists
                    await IntegrationLogOperation.CreateJobSummaryLogAsync(
                        result: JobResponseResult.Success,
                        xoiJobSummaryResponse: response.Data,
                        jobId: jobId);

                    return response;
                }

                throw new Exception("Invalid response received from GetJobSummary");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to get job summary for Job ID: {JobId}", jobId);

                // Keep the existing IntegrationLogOperation call since it exists
                await IntegrationLogOperation.CreateJobSummaryLogAsync(
                    result: JobResponseResult.Failure,
                    message: ex.Message,
                    jobId: jobId);

                throw;
            }
        }
     


        private dynamic GetVariables(JobRelatedData jobRelatedData, string jobId = null)
        {
            var variables = new
            {
                id = jobId,
                assigneeIds = jobRelatedData.AssigneeIds,
                customerName = jobRelatedData.CustomerName,
                jobLocation = jobRelatedData.JobLocation,
                workOrderNumber = jobRelatedData.OrderNumber,
                label = jobRelatedData.Label,
                tags = jobRelatedData.Tags,
                tagSuggestions = jobRelatedData.TagSuggestions,
                internalNoteText = jobRelatedData.InternalNote
            };

            return variables;
        }

        // Method to clear cache if needed
        public void ClearJobSummaryCache()
        {
            jobSummaryCache.Clear();
            _log.LogInformation("Job summary cache cleared");
        }

        // Method to remove specific job from cache
        public void RemoveJobSummaryFromCache(string jobId)
        {
            if (jobSummaryCache.ContainsKey(jobId))
            {
                jobSummaryCache.Remove(jobId);
                _log.LogInformation("Job summary removed from cache for Job ID: {JobId}", jobId);
            }
        }
    }
}

///*using Google.Protobuf.WellKnownTypes;
//using GraphQL;
//using Microsoft.Extensions.Logging;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using XOI_Integration.DataFactory.BaseObject;
//using XOI_Integration.DataModels.Enums;
//using XOI_Integration.DataverseRepository.Operations;
//using XOI_Integration.XOiRepository.Helper;
//using XOI_Integration.XOiRepository.Provider;
//using XOI_Integration.XOiRepository.XOiDataModels;

//namespace XOI_Integration.XOiRepository
//{
//    public class XOiOperation
//    {
//        private static readonly Dictionary<OperationType, string> requests = new Dictionary<OperationType, string>()
//        {
//            {OperationType.Create, @"mutation CreateJob(
//              $assigneeIds: [ID!]!,
//              $customerName: String!,
//              $jobLocation: String!,
//              $workOrderNumber: String!,
//              $label: String,
//              $tags: [String!],
//              $tagSuggestions: [String!],
//              $internalNoteText: String!
//            ) {
//              createJob(
//                input: {
//                  newJob: {
//                    assigneeIds: $assigneeIds
//                    customerName: $customerName
//                    jobLocation: $jobLocation
//                    workOrderNumber: $workOrderNumber
//                    label: $label
//                    tags: $tags
//                    tagSuggestions: $tagSuggestions
//                    internalNote: { text: $internalNoteText }
//                  }
//                  additionalActions: { createPublicShare: { enabled: true } }
//                }
//              ) {
//                job {
//                  id
//                  createdAt
//                  createdBy
//                  assigneeIds
//                  customerName
//                  jobLocation
//                  workOrderNumber
//                  label
//                  tags
//                  tagSuggestions
//                  deepLinks {
//                     visionWeb {
//                      viewJob {
//                        url
//                      }
//                    }
//                    visionMobile {
//                      editJob {
//                        url
//                      }
//                      jobLocationActivitySearch {
//                        url
//                      }
//                    }
//                  }
//                }
//                additionalActionsResults {
//                  createPublicShare {
//                    shareLink
//                  }
//                }
//              }
//            }" },
//            {OperationType.Update, @"mutation UpdateJob(
//              $id: ID!,
//              $customerName: String!,
//              $jobLocation: String!,
//              $workOrderNumber: String!,
//              $label: String,
//              $tags: [String!],
//              $tagSuggestions: [String!],
//              $internalNoteText: String!,
//              $assigneeIds: [ID!]!
//            ) {
//              updateJob(
//                input: {
//                  id: $id
//                  fieldUpdates: {
//                    customerName: $customerName
//                    jobLocation: $jobLocation
//                    workOrderNumber: $workOrderNumber
//                    label: $label
//                    tags: $tags
//                    tagSuggestions: $tagSuggestions
//                    internalNote: { text: $internalNoteText }
//                    assigneeIds: $assigneeIds
//                  }
//                }
//              ) {
//                job {
//                  id
//                  createdAt
//                  createdBy
//                  assigneeIds
//                  customerName
//                  jobLocation
//                  workOrderNumber
//                  label
//                  tags
//                  tagSuggestions
//                  internalNote {
//                    text
//                  }
//                  deepLinks {
//                    visionWeb {
//                      viewJob {
//                        url
//                      }
//                    }
//                    visionMobile {
//                      viewJob {
//                        url
//                      }
//                      editJob {
//                        url
//                      }
//                      jobLocationActivitySearch {
//                        url
//                      }
//                    }
//                  }
//                }
//              }
//            }" },
//            {OperationType.GetJobSummary, @"query GetJobSummary(
//              $id: ID!, $workflowId: ID) {
//              getJobSummary(input: { jobId: $id, workflowJobId: $workflowId }) {
//                nextToken
//                jobSummary {
//                  jobId
//                  documentation {
//                    workflowName
//                    traits
//                    tags
//                    note {
//                      text
//                    }
//                    choice {
//                      chosen
//                    }
//                    derivedData {
//                      make
//                      model
//                      serial
//                      transcript
//                      manufacture_date
//                    }
//                    workSummary {
//                        summary_text
//                    }
//                  }
//                  assignees {
//                    id
//                    email
//                    given_name
//                    family_name
//                  }
//                }
//              }
//            }" },
//            {OperationType.GetJob, @"query GetJob(
//                $id: ID!) {
//                getJob(input: { id: $id }) {
//                  job {
//                    id
//                    createdAt
//                    createdBy
//                    assigneeIds
//                    customerName
//                    jobLocation
//                    workOrderNumber
//                    label
//                    tags
//                    tagSuggestions
//                    deepLinks {
//                      visionWeb {
//                        viewJob {
//                          url
//                        }
//                      }
//                      visionMobile {
//                        viewJob {
//                          url
//                        }
//                        editJob {
//                          url
//                        }
//                        jobLocationActivitySearch {
//                          url
//                        }
//                      }
//                    }
//                  }
//                }
//              }" }
//        };

//        private Dictionary<string, GraphQLResponse<XOiJobSummaryResponse>> jobSummaryCache = new Dictionary<string, GraphQLResponse<XOiJobSummaryResponse>>();

//        private ILogger _log;
//        private readonly XOiAPI _xoiAPI;

//        public XOiOperation(ILogger log) 
//        { 
//            _log = log;
//            _xoiAPI = new XOiAPI();
//        }

//        public async Task<XOiToBookableResourceData> CreateJobAsync(JobRelatedData jobRelatedData)
//        {
//            _log.LogInformation("Start create job");

//            var query = requests[OperationType.Create];
//            var variables = GetVariables(jobRelatedData);

//            var response = await _xoiAPI.SendRequestAsync<XOiCRUDResponse>(query, variables);

//            _log.LogInformation("Job created");

//            return XOiProcessResponse.BuildXOiToBookableResourceData(_log, OperationType.Create, response);
//        }

//        public async Task<XOiToBookableResourceData> UpdateJobAsync(JobRelatedData jobRelatedData, string jobId)
//        {
//            _log.LogInformation("Start update job");

//            var query = requests[OperationType.Update];
//            var variables = GetVariables(jobRelatedData, jobId);

//            var response = await _xoiAPI.SendRequestAsync<XOiCRUDResponse>(query, variables);

//            _log.LogInformation("Job updated");

//            return XOiProcessResponse.BuildXOiToBookableResourceData(_log, OperationType.Update, response);
//        }

//        public async Task<XOiJobInfo> GetJobAsync(string jobId)
//        {
//            _log.LogInformation("Get Job info from XOi");

//            var query = requests[OperationType.GetJob];
//            var variables = new
//            {
//                id = jobId
//            };

//            var response = await _xoiAPI.SendRequestAsync<XOiCRUDResponse>(query, variables);
           

//            return XOiProcessResponse.BuildXOiJobInfoData(_log, response);
//        }

//        public async Task<List<XOiToCustomerAssetData>> GetJobSummaryAsync(string jobId, string workflowJobId)
//        {
//            var response = await GetJobSummaryResponseAsync(jobId, workflowJobId);

//            return XOiProcessResponse.BuildXOiToCustomerAssetData(_log,response);
//        }

//        public async Task<XOiWorkSummaryToBookableResourceData> GetJobSummaryWorkflowAsync(string jobId, string workflowJobId)
//        {
//            var response = await GetJobSummaryResponseAsync(jobId, workflowJobId);

//            return XOiProcessResponse.BuildXOiWorkSummaryToBookableResourceData(_log, response, workflowJobId);
//        }


//        private async Task<GraphQLResponse<XOiJobSummaryResponse>> GetJobSummaryResponseAsync(string jobId, string workflowJobId)
//        {
//            _log.LogInformation("Start receiving a job summary");

//            if (jobSummaryCache.ContainsKey(jobId))
//            {
//                _log.LogInformation("Finish receiving a job summary");

//                return jobSummaryCache[jobId];
//            }

//            var query = requests[OperationType.GetJobSummary];
//            var variables = new
//            {
//                id = jobId,
//                workflowId = workflowJobId
//            };

//            var response = await _xoiAPI.SendRequestAsync<XOiJobSummaryResponse>(query, variables);

//            if (response.Data != null)
//            {
//                _log.LogInformation("Job summary successfully recieved");

//                jobSummaryCache.Add(jobId, response);

//                await IntegrationLogOperation.CreateJobSummaryLogAsync(result: JobResponseResult.Success, xoiJobSummaryResponse: response.Data, jobId: jobId);

//                return response;
//            }
//            else if (response.Errors != null && response.Errors.Any()) 
//            {
//                var errorMessage = response.Errors.FirstOrDefault().Message;

//                await IntegrationLogOperation.CreateJobSummaryLogAsync(result: JobResponseResult.Failure, message: errorMessage, jobId: jobId);

//                throw new Exception(errorMessage);
//            }

//            _log.LogError("Invalid responce recived");

//            throw new Exception("Invalid responce recived");
//        }


//        private dynamic GetVariables(JobRelatedData jobRelatedData, string jobId = null)
//        {
//            var variables = new
//            {
//                id = jobId,
//                assigneeIds = jobRelatedData.AssigneeIds,
//                customerName = jobRelatedData.CustomerName,
//                jobLocation = jobRelatedData.JobLocation,
//                workOrderNumber = jobRelatedData.OrderNumber,
//                label = jobRelatedData.Label,
//                tags = jobRelatedData.Tags,
//                tagSuggestions = jobRelatedData.TagSuggestions,
//                internalNoteText = jobRelatedData.InternalNote
//            };

//            return variables;
//        }
//    }
//}/*
