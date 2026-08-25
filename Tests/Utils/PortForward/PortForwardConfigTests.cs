using System;
using System.Linq;
using _1RM.Utils.PortForward;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Tests.Utils.PortForward
{
    [TestClass]
    public class PortForwardConfigTests
    {
        [TestInitialize]
        public void Setup() => TestInit.Init();

        private static PortForwardConfig NewLocalForward() => new PortForwardConfig
        {
            Name = "proxmox",
            Type = EPortForwardType.Local,
            SshHostName = "bastion",
            BoundAddress = "127.0.0.1",
            BoundPort = 8006,
            DestinationHost = "10.0.0.5",
            DestinationPort = 8006,
        };

        [TestMethod]
        public void AFullyDescribedForwardIsUsable()
        {
            Assert.IsNull(NewLocalForward().Validate());
        }

        [TestMethod]
        public void AForwardWithNoHostCannotStart()
        {
            var forward = NewLocalForward();
            forward.SshHostName = "";

            Assert.AreEqual("port_forward_invalid_no_host", forward.Validate());
        }

        [TestMethod]
        public void PortsOutsideTheValidRangeAreRejected()
        {
            var forward = NewLocalForward();

            forward.BoundPort = 0;
            Assert.AreEqual("port_forward_invalid_bound_port", forward.Validate());

            forward.BoundPort = 70000;
            Assert.AreEqual("port_forward_invalid_bound_port", forward.Validate());

            forward.BoundPort = 8006;
            forward.DestinationPort = -1;
            Assert.AreEqual("port_forward_invalid_destination_port", forward.Validate());
        }

        [TestMethod]
        public void ADynamicForwardNeedsNoDestination()
        {
            var forward = NewLocalForward();
            forward.DestinationHost = "";
            forward.DestinationPort = 0;

            Assert.IsNotNull(forward.Validate(), "a local forward has nowhere to go without one");

            forward.Type = EPortForwardType.Dynamic;

            Assert.IsFalse(forward.NeedsDestination);
            Assert.IsNull(forward.Validate(), "SOCKS callers name their own destination per connection");
        }

        [TestMethod]
        public void BindingOffLoopbackIsFlaggedAsExposed()
        {
            var forward = NewLocalForward();
            Assert.IsFalse(forward.IsExposedToNetwork);

            forward.BoundAddress = "0.0.0.0";
            Assert.IsTrue(forward.IsExposedToNetwork);

            forward.BoundAddress = "127.0.0.1";
            Assert.IsFalse(forward.IsExposedToNetwork);
        }

        [TestMethod]
        public void SummaryShowsTheRouteInTheDirectionTrafficTravels()
        {
            var forward = NewLocalForward();
            StringAssert.Contains(forward.Summary, "127.0.0.1:8006 -> 10.0.0.5:8006");
            StringAssert.Contains(forward.Summary, "bastion");

            forward.Type = EPortForwardType.Dynamic;
            StringAssert.Contains(forward.Summary, "SOCKS");

            forward.Type = EPortForwardType.Remote;
            StringAssert.Contains(forward.Summary, "bastion:8006 -> 10.0.0.5:8006");
        }

        [TestMethod]
        public void RuntimeStateIsNotWrittenToTheProfile()
        {
            var forward = NewLocalForward();
            forward.Status = EPortForwardStatus.Failed;
            forward.LastError = "connection refused by the bastion";

            var json = JsonConvert.SerializeObject(forward);

            Assert.IsFalse(json.Contains("connection refused"), "an error from the last run is not configuration");
            Assert.IsFalse(json.Contains("Status"), "nor is whether it happened to be up when the app closed");

            var restored = JsonConvert.DeserializeObject<PortForwardConfig>(json)!;
            Assert.AreEqual(EPortForwardStatus.Stopped, restored.Status);
            Assert.AreEqual("", restored.LastError);
            Assert.AreEqual("10.0.0.5", restored.DestinationHost, "but the route itself survives");
            Assert.AreEqual(EPortForwardType.Local, restored.Type);
        }

        [TestMethod]
        public void AutoStartSurvivesARoundTrip()
        {
            var forward = NewLocalForward();
            forward.AutoStart = true;

            var restored = JsonConvert.DeserializeObject<PortForwardConfig>(JsonConvert.SerializeObject(forward))!;

            Assert.IsTrue(restored.AutoStart);
        }

        [TestMethod]
        public void StatusDrivesTheFlagsTheListBindsTo()
        {
            var forward = NewLocalForward();
            Assert.IsFalse(forward.IsRunning);
            Assert.IsFalse(forward.IsFailed);

            forward.Status = EPortForwardStatus.Running;
            Assert.IsTrue(forward.IsRunning);
            Assert.IsFalse(forward.IsFailed);

            forward.Status = EPortForwardStatus.Failed;
            Assert.IsFalse(forward.IsRunning);
            Assert.IsTrue(forward.IsFailed);
        }

        [TestMethod]
        public void EveryDirectionHasALabelNamingItsOpenSshFlag()
        {
            foreach (EPortForwardType type in Enum.GetValues(typeof(EPortForwardType)))
            {
                var name = PortForwardTypeName.Of(type);
                Assert.AreNotEqual(type.ToString(), name, $"{type} still falls back to its enum name");
                Assert.IsTrue(name.Contains("-"), $"{type} should name the ssh flag it corresponds to");
            }
        }

        [TestMethod]
        public void ChangingTypeNotifiesTheFieldsTheEditorShowsAndHides()
        {
            var forward = NewLocalForward();
            var notified = new System.Collections.Generic.List<string>();
            forward.PropertyChanged += (_, e) => notified.Add(e.PropertyName ?? "");

            forward.Type = EPortForwardType.Dynamic;

            CollectionAssert.Contains(notified, nameof(PortForwardConfig.NeedsDestination));
            CollectionAssert.Contains(notified, nameof(PortForwardConfig.Summary));
        }
    }
}
