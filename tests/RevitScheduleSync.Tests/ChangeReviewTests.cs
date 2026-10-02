using System.Collections.Generic;
using System.Linq;
using RevitScheduleSync.Models;
using RevitScheduleSync.Models.Import;
using RevitScheduleSync.Services;
using RevitScheduleSync.UI.ViewModels;
using Xunit;

namespace RevitScheduleSync.Tests
{
    public class ChangeReviewTests
    {
        private sealed class Prompts : IUserPrompts
        {
            public bool Answer { get; set; }
            public bool Throw { get; set; }
            public int Asked { get; private set; }

            public bool Confirm(string instruction, string content, string confirmText, string cancelText)
            {
                Asked++;
                if (Throw)
                {
                    throw new System.InvalidOperationException("dialog failed");
                }

                return Answer;
            }
        }

        /// <summary>Runs work queued on this thread's dispatcher (the type-approval dialog is deferred).</summary>
        private static void DoEvents()
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new System.Windows.Threading.DispatcherOperationCallback(f => { ((System.Windows.Threading.DispatcherFrame)f!).Continue = false; return null; }),
                frame);
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        private static ChangeItem Item(string sheet, ChangeStatus status, bool type = false, ReviewDecision decision = ReviewDecision.None) => new()
        {
            Status = status,
            SheetName = sheet,
            ScheduleName = sheet,
            ElementUniqueId = System.Guid.NewGuid().ToString(),
            ElementId = 1,
            ElementLabel = "e",
            FieldId = 10,
            ParameterName = "Comments",
            ParameterKey = "k",
            IsType = type,
            TypeName = "T",
            Original = "a",
            Current = "a",
            Excel = "b",
            Target = TargetValue.OfText("b"),
            Decision = decision
        };

        private static ChangeReviewViewModel Review(Prompts prompts, out List<ChangeItem> items)
        {
            items =
            [
                Item("Door Schedule", ChangeStatus.Change, decision: ReviewDecision.Apply),
                Item("Door Schedule", ChangeStatus.Change, decision: ReviewDecision.Apply),
                Item("Door Schedule", ChangeStatus.NoChange),
                Item("Window Schedule", ChangeStatus.Change, decision: ReviewDecision.Apply),
                Item("Window Schedule", ChangeStatus.TypeChange, type: true, decision: ReviewDecision.Pending)
            ];

            var workbook = new ImportWorkbook
            {
                FilePath = "x.xlsx",
                SchemaVersion = 1,
                PluginVersion = "t",
                RevitVersion = "2026",
                ExportedUtc = null,
                Fingerprint = new DocumentFingerprint { Title = "Test" },
                Sheets =
                [
                    Sheet("Door Schedule"),
                    Sheet("Window Schedule"),
                    Sheet("Empty Schedule")
                ],
                Issues = []
            };

            var changes = new ChangeSet
            {
                Workbook = workbook,
                Fingerprint = new FingerprintResult(FingerprintMatch.Match, []),
                Items = items,
                Issues = [],
                ElapsedMilliseconds = 1
            };

            return new ChangeReviewViewModel(changes, prompts, () => { }, () => { }, _ => { }, (_, _) => { });
        }

        private static ImportSheet Sheet(string name) => new()
        {
            SheetName = name,
            ScheduleUniqueId = name,
            ScheduleName = name,
            Mode = Models.Export.SheetMode.Editable,
            SheetFound = true,
            Columns = [],
            Rows = []
        };

        [Fact]
        public void Tabs_list_all_schedules_then_each_schedule_with_changes()
        {
            var review = Review(new Prompts(), out _);

            Assert.Equal(["All schedules (4)", "Door Schedule (2)", "Window Schedule (2)"], review.ScheduleTabs.Select(t => t.Label));
        }

        [Fact]
        public void Selecting_a_tab_scopes_grid_and_cards_but_not_apply()
        {
            var review = Review(new Prompts(), out _);
            review.SelectedTab = review.ScheduleTabs[2]; // Window Schedule

            Assert.Equal(2, review.Items.Cast<ChangeRowViewModel>().Count());
            Assert.All(review.Items.Cast<ChangeRowViewModel>(), r => Assert.Equal("Window Schedule", r.Item.SheetName));
            Assert.Equal(1, review.ChangeCount);
            Assert.Equal(1, review.TypeChangeCount);
            Assert.Equal(1, review.WillWriteCount);
            Assert.Equal(3, review.TotalWillWriteCount);          // Apply covers every schedule
            Assert.Contains("3 change(s)", review.ApplyText);
        }

        [Fact]
        public void Bulk_skip_only_affects_the_selected_tab()
        {
            var review = Review(new Prompts(), out var items);
            review.SelectedTab = review.ScheduleTabs[1]; // Door Schedule
            review.SkipAllChangesCommand.Execute(null);

            Assert.All(items.Where(i => i.SheetName == "Door Schedule" && i.Status == ChangeStatus.Change),
                i => Assert.Equal(ReviewDecision.Skip, i.Decision));
            Assert.Equal(ReviewDecision.Apply, items[3].Decision); // window change untouched
            Assert.Equal(1, review.TotalWillWriteCount);
        }

        [Fact]
        public void Type_change_needs_confirmation_and_cancel_keeps_it_pending()
        {
            var prompts = new Prompts { Answer = false };
            var review = Review(prompts, out var items);
            var row = review.Items.Cast<ChangeRowViewModel>().Single(r => r.Item.Status == ChangeStatus.TypeChange);

            row.SelectedAction = row.Actions.Single(a => a.Decision == ReviewDecision.Apply);
            Assert.Equal(0, prompts.Asked); // never inside the ComboBox binding update
            DoEvents();
            Assert.Equal(1, prompts.Asked);
            Assert.Equal(ReviewDecision.Pending, items[4].Decision);
            Assert.Equal(ReviewDecision.Pending, row.SelectedAction!.Decision);

            prompts.Answer = true;
            row.SelectedAction = row.Actions.Single(a => a.Decision == ReviewDecision.Apply);
            DoEvents();
            Assert.Equal(ReviewDecision.Apply, items[4].Decision);
            Assert.Equal(4, review.TotalWillWriteCount);
        }

        [Fact]
        public void Failing_type_approval_dialog_is_reported_not_swallowed()
        {
            var prompts = new Prompts { Throw = true };
            string? reported = null;
            var review = Review(prompts, out var items);
            review = new ChangeReviewViewModel(review.Changes, prompts, () => { }, () => { }, _ => { }, (message, _) => reported = message);
            var row = review.Items.Cast<ChangeRowViewModel>().Single(r => r.Item.Status == ChangeStatus.TypeChange);

            row.SelectedAction = row.Actions.Single(a => a.Decision == ReviewDecision.Apply);
            DoEvents();

            Assert.Equal("The type change could not be approved.", reported);
            Assert.Equal(ReviewDecision.Pending, items[4].Decision);
        }
    }
}
