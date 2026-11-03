using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace AOLauncher
{
    [TestClass]
    public sealed class PluginStartupDiagnosticsTests
    {
        readonly List<string> records = new List<string>();
        DateTime now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        FailureDiagnostics diagnostics;
        string directory;

        [TestInitialize]
        public void Initialize()
        {
            directory = Path.Combine(Path.GetTempPath(), "NexusUnitTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            diagnostics = new FailureDiagnostics((area, text) => records.Add(text), () => now);
        }

        [TestCleanup]
        public void Cleanup() => Directory.Delete(directory, true);

        [TestMethod]
        public void HostLaunchExceptionBecomesFailedResponseWithActionableEvidence()
        {
            var transport = new FakeTransport {
                ExchangeResult = () => throw new TimeoutException(),
                Launch = () => throw new Win32Exception(5, "Access denied")
            };
            var response = Send(transport);
            Assert.IsFalse(response.Ok);
            Assert.AreEqual("Failed", response.State);
            StringAssert.Contains(records.Single(), "pid=42 start=100");
            StringAssert.Contains(records.Single(), "Host launch failed; no injection dispatched");
            StringAssert.Contains(records.Single(), "Win32 error 5");
            Assert.AreEqual(1, transport.Starts);
            Assert.AreEqual(1, transport.Exchanges);
        }

        [TestMethod]
        public void IdleStatusWithoutHostIsQuietAndNeverStartsIt()
        {
            var transport = new FakeTransport { ExchangeResult = () => throw new TimeoutException() };
            var response = PluginHostClient.Send(new PluginHostClient.Request { Command = "status" }, null, false, 1, transport, diagnostics);
            Assert.IsNull(response);
            Assert.AreEqual(0, records.Count);
            Assert.AreEqual(0, transport.Starts);
        }

        [TestMethod]
        public void HostConnectionRetriesOnceAfterLaunchThenReportsFailure()
        {
            var transport = new FakeTransport { ExchangeResult = () => throw new TimeoutException("Host unavailable") };
            Assert.IsNull(Send(transport));
            Assert.AreEqual(2, transport.Exchanges);
            Assert.AreEqual(1, transport.Starts);
            StringAssert.Contains(records.Single(), "Host connection unavailable");
        }

        [TestMethod]
        public void DispatchTimeoutDoesNotResendOrClaimInitializationSucceeded()
        {
            var transport = new FakeTransport { ExchangeResult = () => new PluginHostClient.Response { Error = "No reply" } };
            Assert.IsFalse(Send(transport).Ok);
            Assert.AreEqual(1, transport.Exchanges);
            Assert.AreEqual(0, transport.Starts);
            StringAssert.Contains(records.Single(), "no automatic resend after dispatch");
        }

        [TestMethod]
        public void RecoveryIsRecordedOnceAndExplicitlyDoesNotConfirmInitialization()
        {
            var transport = new FakeTransport { ExchangeResult = () => throw new IOException("Pipe closed") };
            Send(transport);
            transport.ExchangeResult = () => new PluginHostClient.Response { Ok = true };
            Assert.IsTrue(Send(transport).Ok);
            Assert.IsTrue(Send(transport).Ok);
            Assert.AreEqual(2, records.Count);
            StringAssert.Contains(records[1], "initialization is checked separately");
        }

        [TestMethod]
        public void InitializationFailureFromActualReportReachesDiagnosticsAndRecovery()
        {
            var client = Client();
            WriteReport(client, "Failed", "System.InvalidOperationException: fixture initialization failed");
            var checks = Read(client);
            Assert.AreEqual(PluginStatus.Failed, checks.Single().Status);
            StringAssert.Contains(records.Single(), "plugin=Fixture");
            StringAssert.Contains(records.Single(), "fixture initialization failed");
            Read(client);
            Assert.AreEqual(1, records.Count);
            WriteReport(client, "Started", null);
            Assert.AreEqual(PluginStatus.Confirmed, Read(client).Single().Status);
            StringAssert.Contains(records.Last(), "recovered=Initialization acknowledged suppressed=1");
        }

        [TestMethod]
        public void MissingReportIsPendingWithoutInventedFailure()
        {
            Assert.IsNull(Read(Client()));
            Assert.AreEqual(0, records.Count);
        }

        [TestMethod]
        public void WrongSessionCannotAcknowledgeOrReportAnotherClientsFailure()
        {
            var client = Client();
            WriteReport(client, "Failed", "old process", requestId: "previous");
            Assert.IsNull(Read(client));
            Assert.AreEqual(0, records.Count);
        }

        [TestMethod]
        public void MalformedReportIsVisibleWithoutLoggingRawInput()
        {
            var client = Client();
            File.WriteAllText(ReportPath(client), "{ secret-token-do-not-record");
            Assert.IsNull(Read(client));
            StringAssert.Contains(records.Single(), "Invalid startup report");
            Assert.IsFalse(records.Single().Contains("secret-token-do-not-record"));
        }

        [TestMethod]
        public void OversizedReportIsRejectedWithEvidence()
        {
            var client = Client();
            File.WriteAllText(ReportPath(client), new string('x', 512 * 1024 + 1));
            Assert.IsNull(Read(client));
            StringAssert.Contains(records.Single(), "exceeds 512 KiB");
        }

        [TestMethod]
        public void RepeatedPollingIsRateLimitedAndReportsSuppressedCount()
        {
            for (int i = 0; i < 100; i++) diagnostics.Report("poll", "retry", detail: "same failure");
            Assert.AreEqual(1, records.Count);
            now = now.AddSeconds(30);
            diagnostics.Report("poll", "retry", detail: "same failure");
            Assert.AreEqual(2, records.Count);
            StringAssert.Contains(records.Last(), "suppressed=99");
        }

        [TestMethod]
        public void ChangedFailureAndReusedPidAreNotHiddenBySuppression()
        {
            diagnostics.Report("load", "failed", detail: "first", pid: 42, startTicks: 100);
            diagnostics.Report("load", "failed", detail: "changed", pid: 42, startTicks: 100);
            diagnostics.Report("load", "failed", detail: "changed", pid: 42, startTicks: 200);
            Assert.AreEqual(3, records.Count);
        }

        [TestMethod]
        public void DiagnosticSinkFailureCannotBreakPluginRequest()
        {
            var broken = new FailureDiagnostics((area, text) => throw new IOException("disk full"));
            var response = PluginHostClient.Send(new PluginHostClient.Request { Command = "inject" }, null, true, 1,
                new FakeTransport { ExchangeResult = () => throw new IOException("Pipe failed") }, broken);
            Assert.IsFalse(response.Ok);
        }

        [TestMethod]
        public void RequestsAndPluginPathsAreNotDumpedIntoHostFailureLog()
        {
            var request = new PluginHostClient.Request { Command = "inject", Pid = 42, StartTicks = 100,
                Label = "private-label", Plugins = new[] { "C:\\private-folder\\secret-plugin.dll" } };
            PluginHostClient.Send(request, null, false, 1,
                new FakeTransport { ExchangeResult = () => throw new IOException("Pipe failed") }, diagnostics);
            Assert.IsFalse(records.Single().Contains("private-label"));
            Assert.IsFalse(records.Single().Contains("private-folder"));
        }

        PluginHostClient.Response Send(FakeTransport transport) => PluginHostClient.Send(
            new PluginHostClient.Request { Command = "inject", Pid = 42, StartTicks = 100 },
            new AOSharpPack { AOSharpDirectory = directory }, true, 1, transport, diagnostics);
        PluginHostClient.ClientState Client() => new PluginHostClient.ClientState {
            Pid = 42, StartTicks = 100, RequestId = "current", Plugins = new[] { Path.Combine(directory, "Fixture.dll") }
        };
        string ReportPath(PluginHostClient.ClientState client) => Path.Combine(directory, client.Pid + "-" + client.StartTicks + ".json");
        List<PluginCheck> Read(PluginHostClient.ClientState client) => PluginRuntimeReport.Read(client, path => "Fixture", directory, diagnostics);
        void WriteReport(PluginHostClient.ClientState client, string state, string error, string requestId = "current")
        {
            File.WriteAllText(ReportPath(client), new JavaScriptSerializer().Serialize(new PluginRuntimeReport.Report {
                Format = 1, Pid = client.Pid, StartTicks = client.StartTicks, RequestId = requestId,
                Plugins = new List<PluginRuntimeReport.Entry> {
                    new PluginRuntimeReport.Entry { Path = client.Plugins[0], State = state, Error = error }
                }
            }));
        }
        sealed class FakeTransport : PluginHostClient.ITransport
        {
            internal Func<PluginHostClient.Response> ExchangeResult;
            internal Action Launch = () => { };
            internal int Starts, Exchanges;
            public PluginHostClient.Response Exchange(PluginHostClient.Request request, int connectTimeoutMs, int responseTimeoutMs)
            { Exchanges++; return ExchangeResult(); }
            public void Start(AOSharpPack pack) { Starts++; Launch(); }
        }
    }
}
