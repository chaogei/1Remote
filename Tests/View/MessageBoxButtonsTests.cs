using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.View
{
    /// <summary>
    /// Covers the promise every confirmation in the app relies on: a question always offers a way to say no.
    ///
    /// MessageBoxHelper.Confirm funnels every destructive prompt in the app — deleting a proxy, a credential,
    /// a server, a port forward, restoring a backup — through MessageBoxPageViewModel with
    /// <see cref="MessageBoxButton.YesNo"/>, so the button list built here is the one those prompts show.
    ///
    /// Be aware of what these tests cannot see. The bug that prompted them lived in MessageBoxPageView.xaml,
    /// not in this view model: the No button was in ButtonList all along and was destroyed on the way to the
    /// screen by a Setter that bound Button.Template to a Style. A view model test would have passed
    /// throughout. <see cref="NoDialogButtonTakesItsTemplateFromAStyle"/> is the one below that would have
    /// caught it, and it reads the XAML as text because there is no way to render WPF in a unit test.
    /// </summary>
    [TestClass]
    public class MessageBoxButtonsTests
    {
        private static _1RM.View.Utils.MessageBoxPageViewModel Confirmation(
            MessageBoxButton buttons = MessageBoxButton.YesNo,
            IDictionary<MessageBoxResult, string>? labels = null)
        {
            var vm = new _1RM.View.Utils.MessageBoxPageViewModel();
            vm.Setup("delete the selected items?", "Warning", buttons, MessageBoxImage.Question, buttonLabels: labels);
            return vm;
        }

        [TestMethod]
        public void AYesNoConfirmationOffersBothAnswersInOrder()
        {
            var vm = Confirmation();

            Assert.IsNotNull(vm.ButtonList);
            Assert.AreEqual(2, vm.ButtonList!.Count, "a confirmation with a single action is a trap");
            Assert.AreEqual(MessageBoxResult.Yes, vm.ButtonList[0].Value);
            Assert.AreEqual(MessageBoxResult.No, vm.ButtonList[1].Value);
        }

        [TestMethod]
        public void TheCallersLabelsAreUsedForBothAnswers()
        {
            // The labels MessageBoxHelper.Confirm passes: whatever the language service translated.
            var vm = Confirmation(labels: new Dictionary<MessageBoxResult, string>
            {
                { MessageBoxResult.Yes, "是" },
                { MessageBoxResult.No, "否" },
            });

            Assert.AreEqual("是", vm.ButtonList![0].Label);
            Assert.AreEqual("否", vm.ButtonList[1].Label);
        }

        [TestMethod]
        public void DismissingAConfirmationAnswersNo()
        {
            var vm = Confirmation();

            vm.CancelClicked();

            Assert.AreEqual(MessageBoxResult.No, vm.ClickedButton, "Escape must never mean yes on a delete prompt");
        }

        [TestMethod]
        public void TheCancelButtonOfAConfirmationIsTheNegativeAnswer()
        {
            var vm = Confirmation();

            Assert.AreEqual(MessageBoxResult.No, vm.CancelButton?.Value);
        }

        [TestMethod]
        public void AThreeWayConfirmationKeepsAllThreeAnswers()
        {
            var vm = Confirmation(MessageBoxButton.YesNoCancel);

            CollectionAssert.AreEqual(
                new[] { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel },
                vm.ButtonList!.Select(b => b.Value).ToArray());
            Assert.AreEqual(MessageBoxResult.Cancel, vm.CancelButton?.Value);
        }

        [TestMethod]
        public void AnOkCancelPromptKeepsItsCancel()
        {
            var vm = Confirmation(MessageBoxButton.OKCancel);

            CollectionAssert.AreEqual(
                new[] { MessageBoxResult.OK, MessageBoxResult.Cancel },
                vm.ButtonList!.Select(b => b.Value).ToArray());
        }

        [TestMethod]
        public void AnAlertIsTheOnlySingleButtonBox()
        {
            // MessageBoxHelper.Alert is the only caller that asks for MessageBoxButton.OK, and one button is
            // right there: it states something rather than asking.
            var vm = Confirmation(MessageBoxButton.OK);

            Assert.AreEqual(1, vm.ButtonList!.Count);
            Assert.AreEqual(MessageBoxResult.OK, vm.ButtonList[0].Value);
            Assert.AreEqual(MessageBoxResult.OK, vm.CancelButton?.Value, "Escape closes an alert through its only button");
        }

        /// <summary>
        /// The regression guard for the actual bug. `{Binding Template, Source={StaticResource SomeStyle}}`
        /// looks like it reuses another button's look; a Style has no Template property, so it resolves to
        /// null and the button it is applied to renders nothing at all. Restyle a button by setting its
        /// Background, Foreground and BorderBrush, or by BasedOn — never by taking a Template off a Style.
        /// </summary>
        [TestMethod]
        public void NoDialogButtonTakesItsTemplateFromAStyle()
        {
            var ui = FindUiSourceDirectory();
            if (ui == null)
            {
                Assert.Inconclusive("the Ui source tree is not next to the test assembly");
                return;
            }

            var offenders = Directory.EnumerateFiles(ui, "*.xaml", SearchOption.AllDirectories)
                .Where(f => File.ReadAllText(f).Contains("{Binding Template, Source="))
                .Select(f => Path.GetFileName(f))
                .ToArray();

            Assert.AreEqual(0, offenders.Length,
                "these files bind a Template to a Style, which resolves to null and erases the control: " + string.Join(", ", offenders));
        }

        private static string? FindUiSourceDirectory()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(MessageBoxButtonsTests).Assembly.Location)!);
            while (dir != null)
            {
                var ui = Path.Combine(dir.FullName, "Ui", "View");
                if (Directory.Exists(ui))
                    return ui;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
