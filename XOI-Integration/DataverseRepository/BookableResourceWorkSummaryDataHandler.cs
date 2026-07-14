//Updated cleaned on 8th feb
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using System;
using System.Linq;
using System.Threading.Tasks;
using XOI_Integration.DataverseRepository.Operations;
using XOI_Integration.DataverseRepository.Provider;
using XOI_Integration.XOiRepository.XOiDataModels;

namespace XOI_Integration.DataverseRepository
{
    public class BookableResourceWorkSummaryDataHandler
    {
        private readonly ILogger _log;

        public BookableResourceWorkSummaryDataHandler(ILogger log)
        {
            _log = log;
        }

        // Invisible start/end markers so we can find the block reliably
        /*//---- to be removed----
        private const char ZW_START = '\u2063'; // Invisible Separator
        private const char ZW_END = '\u2064'; // Invisible Plus

        // Encode 0/1 bits
        private const char ZW_0 = '\u200B'; // zero-width space
        private const char ZW_1 = '\u200C'; // zero-width non-joiner*/

        public static async Task CreateBookableResourceBookingNoteAsync(
            ILogger _log,
            XOiWorkSummaryToBookableResourceData xOiSummary,
            string jobId)
        {
            if (xOiSummary == null)
            {
                _log.LogInformation("No eligible workflow summary to sync - skipping booking note creation");
                return;
            }

            _log.LogInformation("Start creating Bookable Resource Booking Timeline");

            var bookingIds = await BookableResourceBookingOperation.GetBookableResourceBookingIdsAsync(jobId);
            if (bookingIds == null || !bookingIds.Any())
            {
                _log.LogWarning("No booking IDs found. Skipping booking notes.");
                return;
            }

            // ---------------------------------------------------
            // 1. Resolve BU and update owner on the customer asset
            // ---------------------------------------------------
            foreach (var bookingId in bookingIds)
            {
                _log.LogInformation($"[BU] Resolving owning team for booking {bookingId}");

                var owningTeamId = await CustomerAssetOperation.GetOwningTeamFromBookingAsync(_log, bookingId);

                if (owningTeamId.HasValue && xOiSummary.CustomerAssetId != Guid.Empty)
                {
                    Entity updateOwner = new Entity("msdyn_customerasset", xOiSummary.CustomerAssetId)
                    {
                        ["ownerid"] = new EntityReference("team", owningTeamId.Value)
                    };

                    await DataverseApi.Instance.UpdateAsync(updateOwner);
                    _log.LogInformation($"[BU] Asset {xOiSummary.CustomerAssetId} owner set to team {owningTeamId.Value}");
                }
                else if (!owningTeamId.HasValue)
                {
                    _log.LogWarning($"[BU] Owning team not found for booking {bookingId}");
                }
            }

            // ---------------------------------------------------
            // 2. Create Booking Notes (HASH based, append-only, NO visible marker)
            // ---------------------------------------------------

            var allExistingNotes = await BookableResourceBookingOperation.GetBookableResourceBookingNotes(jobId);

            foreach (var bookingId in bookingIds)
            {
                var existingNotesForBooking = allExistingNotes
                    .Where(n => n.BookingId == bookingId)
                    .ToList();

                string jobShareLink = await BookableResourceBookingOperation
                    .GetBookableResourceBookingCustomerJobShareLinkAsync(bookingId);

                // Hash ONLY summary (link changes won't create duplicates)
                string summaryText =
                    $"[{xOiSummary.WorkflowName}] Summary from ({xOiSummary.UserInitial}): {xOiSummary.WorkSummary}";

                string hash = ComputeHash(summaryText);

                string visibleNoteText = summaryText + Environment.NewLine + jobShareLink;
                string storedNoteText = visibleNoteText;

                // Skip if same hash already exists (new system)
                bool alreadyExists = existingNotesForBooking.Any(n =>
    string.Equals(n.Hash, hash, StringComparison.OrdinalIgnoreCase));

                // Optional fallback (helps for old notes created before hidden-hash existed)
                if (!alreadyExists)
                {
                    alreadyExists = existingNotesForBooking.Any(n =>
                        !string.IsNullOrEmpty(n.Note) &&
                        n.Note.Contains(summaryText, StringComparison.OrdinalIgnoreCase));
                }

                if (alreadyExists)
                {
                    _log.LogInformation($"Skipping (no change) — same summary already posted for booking {bookingId}");
                    continue;
                }

                Entity note = new Entity("msdyn_bookableresourcebookingquicknote")
                {
                    ["msdyn_quicknote_lookup_entity"] = new EntityReference("bookableresourcebooking", bookingId),
                    ["msdyn_text"] = storedNoteText,
                    ["acl_xoisummaryhash"] = hash
                };

                await DataverseApi.Instance.CreateAsync(note);
                _log.LogInformation($"Created NEW note (summary changed) for booking {bookingId}");
            }

            _log.LogInformation("Finish creating Bookable Resource Booking Notes");
        }

        private static string ComputeHash(string input)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(input ?? "");
            return Convert.ToHexString(sha.ComputeHash(bytes));
        }
        //--To be Removed
   /*     private static string AppendHiddenHash(string visibleText, string hashHex)
        {
            var hiddenPayload = EncodeHexToZeroWidth(hashHex);

            return (visibleText ?? string.Empty)
                + Environment.NewLine
                + ZW_START
                + hiddenPayload
                + ZW_END;
        }

        private static string ExtractHiddenHash(string noteText)
        {
            if (string.IsNullOrEmpty(noteText)) return null;

            int start = noteText.LastIndexOf(ZW_START);
            if (start < 0) return null;

            int end = noteText.IndexOf(ZW_END, start + 1);
            if (end < 0) return null;

            string payload = noteText.Substring(start + 1, end - start - 1);
            return DecodeZeroWidthToHex(payload);
        }

        private static string EncodeHexToZeroWidth(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return string.Empty;

            // If you are NOT on .NET 5+, replace this with a custom hex parser.
            byte[] bytes = Convert.FromHexString(hex);

            var sb = new System.Text.StringBuilder(bytes.Length * 8);

            foreach (var b in bytes)
            {
                for (int bit = 7; bit >= 0; bit--)
                {
                    bool isOne = ((b >> bit) & 1) == 1;
                    sb.Append(isOne ? ZW_1 : ZW_0);
                }
            }

            return sb.ToString();
        }

        private static string DecodeZeroWidthToHex(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return null;

            var bits = payload.Where(c => c == ZW_0 || c == ZW_1).ToArray();
            if (bits.Length % 8 != 0) return null;

            int byteCount = bits.Length / 8;
            byte[] bytes = new byte[byteCount];

            for (int i = 0; i < byteCount; i++)
            {
                byte value = 0;
                for (int bit = 0; bit < 8; bit++)
                {
                    value <<= 1;
                    if (bits[i * 8 + bit] == ZW_1)
                        value |= 1;
                }
                bytes[i] = value;
            }

            return Convert.ToHexString(bytes);
        }*/
    }
}

/*//replaced code on 06th feb
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using System;   
using System.Linq;
using System.Threading.Tasks;
using XOI_Integration.DataverseRepository.Operations;
using XOI_Integration.DataverseRepository.Provider;
using XOI_Integration.XOiRepository.XOiDataModels;

namespace XOI_Integration.DataverseRepository
{
    public class BookableResourceWorkSummaryDataHandler
    {
        private readonly ILogger _log;

        public BookableResourceWorkSummaryDataHandler(ILogger log)
        {
            _log = log;
        }

        // Updated 08th Feb Invisible marker (zero-width characters). not visible in UI.
        //private const string HashPrefix = "\u200B\u200B\u200B"; // 3x zero-width space

        public static async Task CreateBookableResourceBookingNoteAsync(
            ILogger _log,
            XOiWorkSummaryToBookableResourceData xOiSummary,
            string jobId)
        {
            _log.LogInformation("Start creating Bookable Resource Booking Timeline");

            var bookingIds = await BookableResourceBookingOperation.GetBookableResourceBookingIdsAsync(jobId);
            if (bookingIds == null || !bookingIds.Any())
            {
                _log.LogWarning("No booking IDs found. Skipping booking notes.");
                return;
            }

            // ---------------------------------------------------
            // 1. Resolve BU and update owner on the customer asset
            // ---------------------------------------------------
            foreach (var bookingId in bookingIds)
            {
                _log.LogInformation($"[BU] Resolving owning team for booking {bookingId}");

                var owningTeamId = await CustomerAssetOperation.GetOwningTeamFromBookingAsync(_log, bookingId);

                if (owningTeamId.HasValue)
                {
                    _log.LogInformation($"[BU] Team resolved: {owningTeamId.Value}");

                    if (xOiSummary.CustomerAssetId != Guid.Empty)
                    {
                        Entity updateOwner = new Entity("msdyn_customerasset", xOiSummary.CustomerAssetId)
                        {
                            ["ownerid"] = new EntityReference("team", owningTeamId.Value)
                        };

                        await DataverseApi.Instance.UpdateAsync(updateOwner);
                        _log.LogInformation($"[BU] Asset {xOiSummary.CustomerAssetId} owner set to team {owningTeamId.Value}");
                    }
                }
                else
                {
                    _log.LogWarning($"[BU] Owning team not found for booking {bookingId}");
                }
            }

            // ---------------------------------------------------
            // 2. Create Booking Notes (HASH based, append-only) Updated 8th feb (and No Visible Marker Updated)    
            // ---------------------------------------------------

            // Fetch all notes once (not inside booking loop)
            var allExistingNotes = await BookableResourceBookingOperation.GetBookableResourceBookingNotes(jobId);

            foreach (var bookingId in bookingIds)
            {
                // ✅ IMPORTANT: Replace BookingId with the correct property from your notes DTO
                // Example alternatives: n.RegardingId, n.BookableResourceBookingId, n.LookupEntityId, etc.
                var existingNotesForBooking = allExistingNotes
                    .Where(n => n.BookingId == bookingId)
                    .ToList();

                string jobShareLink = await BookableResourceBookingOperation
                    .GetBookableResourceBookingCustomerJobShareLinkAsync(bookingId);

                // Hash should change for ANY small change -> include everything we want to detect.
                // (If share link changes too often and DON'T want that to create new notes,
                // then remove jobShareLink from noteBody BEFORE hashing.)
                string summaryText =
             $"[{xOiSummary.WorkflowName}] Summary from ({xOiSummary.UserInitial}): {xOiSummary.WorkSummary}";

                //  Hash ONLY summary (link changes won't create duplicates)
                string hash = ComputeHash(summaryText);

                //  Visible note text (NO marker visible)
                string visibleNoteText = summaryText + Environment.NewLine + jobShareLink;

                //  Stored note includes invisible hash at the end (not visible to users)
                string storedNoteText = AppendHiddenHash(visibleNoteText, hash);

                //  Skip if same hash already exists in any note for this booking
                if (existingNotesForBooking.Any(n =>
                string.Equals(ExtractHiddenHash(n.Note), hash, StringComparison.OrdinalIgnoreCase)))
                {
                    _log.LogInformation($"Skipping (no change) — same summary already posted for booking {bookingId}");
                    continue;
                }

                Entity note = new Entity("msdyn_bookableresourcebookingquicknote")
                {
                    ["msdyn_quicknote_lookup_entity"] = new EntityReference("bookableresourcebooking", bookingId),
                    ["msdyn_text"] = storedNoteText
                };

                await DataverseApi.Instance.CreateAsync(note);
                _log.LogInformation($"Created NEW note (summary changed) for booking {bookingId}");
            }

            _log.LogInformation("Finish creating Bookable Resource Booking Notes");
        }

        private static string ComputeHash(string input)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(input ?? "");
            return Convert.ToHexString(sha.ComputeHash(bytes));
        }
        // Invisible start/end markers (different chars so we can find the block reliably)
        private const char ZW_START = '\u2063'; // Invisible Separator
        private const char ZW_END = '\u2064'; // Invisible Plus (rarely used)

        // Encode 0/1 bits
        private const char ZW_0 = '\u200B'; // zero-width space
        private const char ZW_1 = '\u200C'; // zero-width non-joiner

        private static string AppendHiddenHash(string visibleText, string hashHex)
        {
            // Convert the hex hash to fully invisible payload
            var hiddenPayload = EncodeHexToZeroWidth(hashHex);

            // Add as an invisible block at the end (no readable characters)
            return (visibleText ?? string.Empty)
                + Environment.NewLine
                + ZW_START
                + hiddenPayload
                + ZW_END;
        }

        // Extract invisible hash from an existing note (if present)
        private static string ExtractHiddenHash(string noteText)
        {
            if (string.IsNullOrEmpty(noteText)) return null;

            int start = noteText.LastIndexOf(ZW_START);
            if (start < 0) return null;

            int end = noteText.IndexOf(ZW_END, start + 1);
            if (end < 0) return null;

            string payload = noteText.Substring(start + 1, end - start - 1);
            return DecodeZeroWidthToHex(payload);
        }

        private static string EncodeHexToZeroWidth(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return string.Empty;

            // Convert hex string -> bytes
            byte[] bytes = Convert.FromHexString(hex);

            // Convert bytes to bits and encode each bit as a zero-width char
            var sb = new System.Text.StringBuilder(bytes.Length * 8);

            foreach (var b in bytes)
            {
                for (int bit = 7; bit >= 0; bit--)
                {
                    bool isOne = ((b >> bit) & 1) == 1;
                    sb.Append(isOne ? ZW_1 : ZW_0);
                }
            }

            return sb.ToString();
        }

        private static string DecodeZeroWidthToHex(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return null;

            // Only accept ZW_0 / ZW_1 chars
            var bits = payload.Where(c => c == ZW_0 || c == ZW_1).ToArray();
            if (bits.Length % 8 != 0) return null;

            int byteCount = bits.Length / 8;
            byte[] bytes = new byte[byteCount];

            for (int i = 0; i < byteCount; i++)
            {
                byte value = 0;
                for (int bit = 0; bit < 8; bit++)
                {
                    value <<= 1;
                    if (bits[i * 8 + bit] == ZW_1)
                        value |= 1;
                }
                bytes[i] = value;
            }

            return Convert.ToHexString(bytes);
        }
    }
}*/


/*COMMENTED ON 16TH JUNE
 * using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XOI_Integration.DataverseRepository.Operations;
using XOI_Integration.DataverseRepository.Provider;
using XOI_Integration.XOiRepository.XOiDataModels;

namespace XOI_Integration.DataverseRepository
{
    public class BookableResourceWorkSummaryDataHandler
    {
        ILogger _log;

        public BookableResourceWorkSummaryDataHandler(ILogger log)
        {
            _log = log;
        }

        public static async Task CreateBookableResourceBookingNoteAsync(
       ILogger _log,
       XOiWorkSummaryToBookableResourceData xOiSummary,
       string jobId)
        {
            _log.LogInformation("Start creating Bookable Resource Booking Timeline");

            var bookingIds = await BookableResourceBookingOperation.GetBookableResourceBookingIdsAsync(jobId);

            // -------------------------
            // 1. Resolve BU + update owner
            // -------------------------
            foreach (var bookingId in bookingIds)
            {
                _log.LogInformation($"[BU] Resolving owning team for booking {bookingId}");

                var owningTeamId = await CustomerAssetOperation.GetOwningTeamFromBookingAsync(_log, bookingId);

                if (owningTeamId.HasValue)
                {
                    _log.LogInformation($"[BU] Team resolved: {owningTeamId.Value}");

                    if (xOiSummary.CustomerAssetId != Guid.Empty)
                    {
                        Entity updateOwner = new Entity("msdyn_customerasset", xOiSummary.CustomerAssetId)
                        {
                            ["ownerid"] = new EntityReference("team", owningTeamId.Value)
                        };

                        await DataverseApi.Instance.UpdateAsync(updateOwner);
                        _log.LogInformation($"[BU] Asset {xOiSummary.CustomerAssetId} owner set to team {owningTeamId.Value}");
                    }
                }
                else
                {
                    _log.LogWarning($"[BU] Owning team not found for booking {bookingId}");
                }
            }

            // -------------------------
            // 2. Create Booking Notes
            // -------------------------
            foreach (var bookingId in bookingIds)
            {
                var existingNotes = (await BookableResourceBookingOperation.GetBookableResourceBookingNotes(jobId))
                                        .Where(n => n.NoteId == bookingId)
                                        .ToList();

                string jobShareLink = await BookableResourceBookingOperation
                                            .GetBookableResourceBookingCustomerJobShareLinkAsync(bookingId);

                string newNote =
                    $"[{xOiSummary.WorkflowName}] Summary from ({xOiSummary.UserInitial}): {xOiSummary.WorkSummary}"
                    + Environment.NewLine
                    + jobShareLink;

                if (existingNotes.Any(n => BookableResourceBookingOperation.NoteEquals(n.Note, newNote)))
                {
                    _log.LogInformation($"Skipping duplicate note for booking {bookingId}");
                    continue;
                }

                Entity note = new Entity("msdyn_bookableresourcebookingquicknote")
                {
                    ["msdyn_quicknote_lookup_entity"] = new EntityReference("bookableresourcebooking", bookingId),
                    ["msdyn_text"] = newNote
                };

                await DataverseApi.Instance.CreateAsync(note);
                _log.LogInformation($"Created note for booking {bookingId}");
            }

            _log.LogInformation("Finish creating Bookable Resource Booking Notes");
        }
    }
}*/



/*GG
 /* public static async Task CreateBookableResourceBookingNoteAsync(ILogger _log, XOiWorkSummaryToBookableResourceData xOiWorkSummary, string jobId)
          {
              if (xOiWorkSummary == null)
              {
                  _log.LogInformation("No eligible workflow summary to sync - skipping booking note creation");
                  return;
              }

              _log.LogInformation("Start creating Bookable Resource Booking Timeline");

              var bookingIds = await BookableResourceBookingOperation.GetBookableResourceBookingIdsAsync(jobId);

              foreach (var bookableResourceBookingId in bookingIds)
              {
                  var existingNotes = (await BookableResourceBookingOperation.GetBookableResourceBookingNotes(jobId))
                                          .Where(n => n.NoteId == bookableResourceBookingId)
                                          .ToList();

                  string customerJobshareLink = await BookableResourceBookingOperation.GetBookableResourceBookingCustomerJobShareLinkAsync(bookableResourceBookingId);

                  var newNote = $"[{xOiWorkSummary.WorkflowName}] Summary from ({xOiWorkSummary.UserInitial}): {xOiWorkSummary.WorkSummary}" +
                                $"{Environment.NewLine}{customerJobshareLink}";

                  if (existingNotes.Any(n => BookableResourceBookingOperation.NoteEquals(n.Note, newNote)))
                  {
                      _log.LogInformation($"✅ Skipping duplicate note for Booking ID {bookableResourceBookingId}");
                      continue;
                  }

                  _log.LogInformation($"Creating Note for Booking ID {bookableResourceBookingId} | Workflow: {xOiWorkSummary.WorkflowName} | Summary: {xOiWorkSummary.WorkSummary}");

                  Entity note = new Entity("msdyn_bookableresourcebookingquicknote")
                  {
                      ["msdyn_quicknote_lookup_entity"] = new EntityReference("bookableresourcebooking", bookableResourceBookingId),
                      ["msdyn_text"] = newNote
                  };

                  await DataverseApi.Instance.CreateAsync(note);

                  _log.LogInformation($"Finished creating note for booking ID {bookableResourceBookingId}");
              }

              _log.LogInformation("Finish creating Bookable Resource Booking Notes");
          }
      }
}
       var dataverseNotes = await BookableResourceBookingOperation.GetBookableResourceBookingNotes(jobId);

       _log.LogInformation("Finish receiving bookable resource booking notes from Dataverse");

       XOiWorkSummaryToBookableResourceData noteToUpdate = new XOiWorkSummaryToBookableResourceData();
       Guid dataverseNoteId = default;
       bool isDuplicate = false;

       _log.LogInformation("Start preparing note data for Create or Update");

       foreach (var dataverseNote in dataverseNotes)
       {
           if (dataverseNote.Note.Contains(xOiNotes.WorkflowName) && !dataverseNote.Note.Contains(xOiNotes.WorkSummary))
           {
               noteToUpdate = xOiNotes;
               dataverseNoteId = dataverseNote.NoteId;
           }
           else if (dataverseNote.Note.Contains(xOiNotes.WorkflowName))
           {
               isDuplicate = true;
           }
       }

       _log.LogInformation("Finish preparing notes data for Create or Update");

       if (noteToUpdate.IsFilled())
       {
           await BookableResourceBookingOperation.UpdateBookableResourceBookingNoteAsync(_log, xOiNotes, dataverseNoteId, jobId);
       }
       else if (isDuplicate == false)
       {
           await BookableResourceBookingOperation.CreateBookableResourceBookingNoteAsync(_log, xOiNotes, jobId);
       }
       else if (isDuplicate)
       {
           _log.LogInformation("Nothing to Create or Update");
       }
   }*/


