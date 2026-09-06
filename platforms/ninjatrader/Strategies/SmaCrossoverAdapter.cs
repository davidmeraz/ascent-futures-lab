#nullable disable
using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Text;
using Futures.Core;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>Thin platform adapter. All indicator and signal rules are in the shared Core sources.</summary>
    public class FuturesSmaTemplate : Strategy
    {
        private const int DefaultFastPeriod = 10;
        private const int DefaultSlowPeriod = 20;
        private const int DefaultContracts = 1;
        private const string ExportVersion = "__EXPORT_VERSION__";
        private const string SourceIdentity = "__SOURCE_IDENTITY__";
        private SmaCrossoverStrategy rules;
        private TimeZoneInfo platformTimeZone;
        private StreamWriter trace;
        private bool hasPendingOrder;
        private string pendingOrderName;
        private string pendingOrderId;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "FuturesSmaTemplate";
                Description = "Shared SMA crossover compatibility example. Version " + ExportVersion + ".";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                BarsRequiredToTrade = 0;
                IsExitOnSessionCloseStrategy = false;
                StartBehavior = StartBehavior.WaitUntilFlat;
                IsInstantiatedOnEachOptimizationIteration = true;
                FastPeriod = DefaultFastPeriod;
                SlowPeriod = DefaultSlowPeriod;
                Contracts = DefaultContracts;
                ExportTrace = false;
                TraceDirectory = string.Empty;
            }
            else if (State == State.DataLoaded)
            {
                // These NinjaTrader properties are user-configurable after SetDefaults. Fail before
                // consuming a bar when the selected platform behavior differs from the shared model.
                if (Calculate != Calculate.OnBarClose)
                    throw new InvalidOperationException("Phase 1 requires Calculate = OnBarClose.");
                if (BarsRequiredToTrade != 0)
                    throw new InvalidOperationException("Phase 1 requires BarsRequiredToTrade = 0; the shared Core controls indicator warmup.");
                if (IsExitOnSessionCloseStrategy)
                    throw new InvalidOperationException("Phase 1 requires Exit on session close = false; exits must come from the shared Core.");
                if (BarsPeriod.BarsPeriodType != NinjaTrader.Data.BarsPeriodType.Minute || BarsPeriod.Value != 1)
                    throw new InvalidOperationException("Phase 1 requires one-minute bars.");
                rules = new SmaCrossoverStrategy(new StrategyParameters(FastPeriod, SlowPeriod, Contracts));
                platformTimeZone = NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo;
                hasPendingOrder = false;
                pendingOrderName = null;
                pendingOrderId = null;
                if (ExportTrace)
                {
                    string directory = string.IsNullOrWhiteSpace(TraceDirectory)
                        ? Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "FuturesLabTraces")
                        : Path.GetFullPath(TraceDirectory);
                    Directory.CreateDirectory(directory);
                    string path = Path.Combine(directory, Name + "_" + Guid.NewGuid().ToString("N") + ".csv");
                    trace = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
                    trace.AutoFlush = true;
                    trace.WriteLine("TimeUtc,Open,High,Low,Close,Volume,Fast,Slow,Signal,TargetPositionQuantity,PositionQuantity,HasPendingOrder,PlatformTimeZoneId,FastPeriod,SlowPeriod,Quantity,StrategySourceId");
                    Print("Futures Lab trace: " + path);
                }
            }
            else if (State == State.Terminated && trace != null)
            {
                trace.Dispose();
                trace = null;
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0 || rules == null) return;
            var bar = new Bar(ToUtc(Time[0]), Open[0], High[0], Low[0], Close[0], checked((long)Volume[0]));
            int position = SignedPosition();
            var snapshot = rules.OnBarClose(bar, new StrategyContext(position, hasPendingOrder));
            WriteTrace(bar, snapshot, position);
            if (snapshot.OrderIntent == null) return;

            int target = snapshot.OrderIntent.TargetPositionQuantity;
            string name = target == 0 && position > 0 ? "CoreExitLong"
                : target == 0 && position < 0 ? "CoreExitShort"
                : target > 0 && position == 0 ? "CoreLong"
                : target < 0 && position == 0 ? "CoreShort" : null;
            if (name == null) return;

            // Set before calling NinjaTrader: callbacks can occur synchronously during submission.
            hasPendingOrder = true;
            pendingOrderName = name;
            pendingOrderId = null;
            if (name == "CoreExitLong") ExitLong(name, "CoreLong");
            else if (name == "CoreExitShort") ExitShort(name, "CoreShort");
            else if (name == "CoreLong") EnterLong(target, name);
            else EnterShort(-target, name);
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time,
            ErrorCode error, string nativeError)
        {
            // Platform-generated or stale order callbacks must not release the current Core order.
            if (!hasPendingOrder || order == null || order.Name != pendingOrderName) return;
            if (pendingOrderId != null && pendingOrderId != order.OrderId) return;
            if (!string.IsNullOrEmpty(order.OrderId)) pendingOrderId = order.OrderId;
            if (orderState == OrderState.Filled || orderState == OrderState.Cancelled || orderState == OrderState.Rejected)
            {
                hasPendingOrder = false;
                pendingOrderName = null;
                pendingOrderId = null;
            }
            if (orderState == OrderState.Rejected)
                Print("Futures Lab order rejected: " + error + " " + nativeError);
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price,
            int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (rules != null) rules.OnExecution(new ExecutionReport(ToUtc(time), SignedPosition(), price));
        }

        private int SignedPosition()
        {
            return Position.MarketPosition == MarketPosition.Long ? Position.Quantity
                : Position.MarketPosition == MarketPosition.Short ? -Position.Quantity : 0;
        }

        private DateTime ToUtc(DateTime platformTime)
        {
            // NinjaTrader bar times use the platform's configured display zone, not the PC zone
            // and not the exchange Trading Hours zone. Ambiguous times must not be guessed.
            var local = DateTime.SpecifyKind(platformTime, DateTimeKind.Unspecified);
            if (platformTimeZone.IsAmbiguousTime(local) || platformTimeZone.IsInvalidTime(local))
                throw new InvalidOperationException("Ambiguous/invalid platform timestamp. Set NinjaTrader's display time zone to UTC for this comparison.");
            return TimeZoneInfo.ConvertTimeToUtc(local, platformTimeZone);
        }

        private void WriteTrace(Bar bar, StrategySnapshot snapshot, int position)
        {
            if (trace == null) return;
            trace.WriteLine(string.Join(",", new[] {
                bar.CloseTimeUtc.ToString("O", CultureInfo.InvariantCulture), Number(bar.Open), Number(bar.High),
                Number(bar.Low), Number(bar.Close), bar.Volume.ToString(CultureInfo.InvariantCulture),
                NullableNumber(snapshot.Fast), NullableNumber(snapshot.Slow), snapshot.Signal.ToString(),
                snapshot.OrderIntent == null ? "" : snapshot.OrderIntent.TargetPositionQuantity.ToString(CultureInfo.InvariantCulture),
                position.ToString(CultureInfo.InvariantCulture), hasPendingOrder ? "true" : "false", Csv(platformTimeZone.Id),
                FastPeriod.ToString(CultureInfo.InvariantCulture), SlowPeriod.ToString(CultureInfo.InvariantCulture),
                Contracts.ToString(CultureInfo.InvariantCulture), SourceIdentity }));
        }

        private static string Number(double value) { return value.ToString("G17", CultureInfo.InvariantCulture); }
        private static string NullableNumber(double? value) { return value.HasValue ? Number(value.Value) : ""; }
        private static string Csv(string value) { return "\"" + value.Replace("\"", "\"\"") + "\""; }

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Fast period", GroupName = "Parameters", Order = 0)]
        public int FastPeriod { get; set; }

        [NinjaScriptProperty, Range(2, int.MaxValue)]
        [Display(Name = "Slow period", GroupName = "Parameters", Order = 1)]
        public int SlowPeriod { get; set; }

        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name = "Contracts", GroupName = "Parameters", Order = 2)]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Export trace CSV", GroupName = "Diagnostics", Order = 3)]
        public bool ExportTrace { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trace directory (empty = user data)", GroupName = "Diagnostics", Order = 4)]
        public string TraceDirectory { get; set; }
    }
}
