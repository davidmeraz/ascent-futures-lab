#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// =============================================================================
// dav3e_vwap  --  NinjaTrader 8 indicator
//
// dav3e's parity anchored VWAP. The VWAP LINE on CLOSED bars matches the Python
// analyzer exactly (_vwap_values in strategies/delta_pullback.py):
//   - anchors at a fixed CHART-clock time each day (AnchorTime)
//   - typical price = (High + Low + Close) / 3, skips zero-volume bars
//   - blank before the anchor and after DayEndTime (see below)
//   - resets on every chart-date change (mirror of the engine's daily reset)
//   - configurable line colors: warm gold above VWAP, soft violet below
//
// DayEndTime emulates the DATA midnight: the Python engine partitions rows by
// UTC calendar date, so its VWAP goes blank at 00:00 UTC. Set DayEndTime to the
// chart-local time of UTC midnight so both sides stay identical:
//   - chart in UTC-7 (dav3e standard): AnchorTime 063000, DayEndTime 170000
//   - chart in UTC:                    AnchorTime 133000, DayEndTime 000000
// UTC and UTC-7 (Mazatlan) do not observe DST, so these constants never move
// and the anchor is always 13:30 UTC. Avoid chart time zones with DST.
//
// Live extras for MANUAL trading (NOT part of the parity contract):
//   - sigma bands (ShowBands): dashed envelopes from the volume-weighted sigma
//   - corner readout (ShowReadout): distance to VWAP in points and the engine's
//     canonical slope (vwap[0] - vwap[SlopeLookbackBars]), plus an EXT flag
//   - gold dot on the line when |Close - VWAP| >= ExtendedDistancePoints (the
//     engine's extended_aligned_vwap location context, default 6.0)
//
// Calculation model (fold + preview) keeps closed-bar parity in ANY Calculate
// mode: accumulators only ever integrate CLOSED bars (folded on the first tick
// of the next bar, then the closed bar is re-stamped with its final value);
// the in-progress bar is shown from a snapshot that never mutates state.
// =============================================================================

namespace NinjaTrader.NinjaScript.Indicators
{
    public class dav3e_vwap : Indicator
    {
        private double cumPv, cumVol, cumPv2;
        private bool   anchored, sawIdleBar, prevBarAccum;
        private int    currentDay, barsToday, anchorInt, dayEndInt;
        private Series<double> vwapSeries;
        private volatile int lastCalculatedBar = -1;
        private SimpleFont readoutFont;
        private volatile ReadoutSnapshot readoutSnapshot;
        private SharpDX.Direct2D1.Brush panelBgDx, panelBorderDx, panelTextDx,
            panelLabelDx, panelUpDx, panelDownDx, panelExtendedDx;
        private SharpDX.DirectWrite.TextFormat panelTextFormat, panelTitleFormat;
        private float panelRenderScale = -1;

        // Una sola referencia publica juntos los textos de la misma actualizacion.
        private sealed class ReadoutSnapshot
        {
            public readonly string Title, Side, Distance, ChangeLabel, Change;
            public readonly int SideSign;
            public readonly bool Extended;

            public ReadoutSnapshot(string title, string side, string distance,
                string changeLabel, string change, int sideSign, bool extended)
            {
                Title = title; Side = side; Distance = distance;
                ChangeLabel = changeLabel; Change = change;
                SideSign = sideSign; Extended = extended;
            }
        }

        // Paleta para graficos oscuros: linea principal destacada y contexto suave.
        private static readonly Brush DefaultUpLineBrush = CreateFrozen(244, 196, 95);
        private static readonly Brush DefaultDownLineBrush = CreateFrozen(210, 151, 231);
        private static readonly Brush DefaultUpCloudBrush = CreateFrozen(54, 170, 144);
        private static readonly Brush DefaultDownCloudBrush = CreateFrozen(194, 84, 105);
        private static readonly Brush InnerBandBrush = CreateFrozen(155, 172, 194);
        private static readonly Brush OuterBandBrush = CreateFrozen(110, 129, 153);
        private static readonly Brush ExtBrush = CreateFrozen(245, 188, 93);
        private static readonly Brush ReadoutTextBrush = CreateFrozen(222, 230, 242);
        private static readonly Brush ReadoutBackgroundBrush = CreateFrozen(17, 24, 34);
        private static readonly Brush ReadoutBorderBrush = CreateFrozen(53, 67, 86);

        private static Brush CreateFrozen(byte r, byte g, byte b)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        // Recursos Direct2D para la nube (sombreado precio<->VWAP). Se crean/
        // liberan en OnRenderTargetChanged (ciclo de vida de DX de NinjaTrader).
        private SharpDX.Direct2D1.Brush cloudUpDx, cloudDownDx;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "dav3e_vwap";
                Description = "dav3e parity anchored VWAP (chart-time anchor, HLC3, data-midnight cutoff) with sigma bands, distance/slope readout and extended marker.";
                IsOverlay   = true;
                IsSuspendedWhileInactive = true;
                Calculate   = Calculate.OnPriceChange;
                AnchorTime  = "063000";   // 06:30 chart time (UTC-7 charts) = 13:30 UTC
                DayEndTime  = "170000";   // 17:00 chart time (UTC-7) = 00:00 UTC = data midnight
                ShowBands   = true;
                Band1Mult   = 1.0;
                Band2Mult   = 2.0;
                ShowReadout = true;
                Position    = TextPosition.TopLeft;   // TopRight = dav3e_atr, BottomRight = dav3e_ticks
                FontSize    = 12;
                ShowReadoutPanel = true;
                ReadoutOpacityPct = 85;
                UseAlignedPanels = true;
                PanelScale = 1.0;
                PanelLeftMargin = 20;
                LineAboveBrush = DefaultUpLineBrush;
                LineBelowBrush = DefaultDownLineBrush;
                CloudAboveBrush = DefaultUpCloudBrush;
                CloudBelowBrush = DefaultDownCloudBrush;
                ShowCloud       = true;
                CloudOpacityPct = 16;   // Relleno tenue para conservar la lectura de las velas.
                SlopeLookbackBars      = 10;    // engine: vwap_slope_lookback_bars
                ExtendedDistancePoints = 6.0;   // engine: extended_vwap_distance_points (0 = off)
                ShowExtendedMarker     = false;   // la senal EXT vive en el readout
                // VWAP principal de 3 px; bandas de contexto mas discretas.
                // Los estilos de cada plot siguen editables desde NinjaTrader.
                AddPlot(new Stroke(LineAboveBrush, 3),                             PlotStyle.Line, "VWAP");      // 0
                AddPlot(new Stroke(InnerBandBrush, DashStyleHelper.Dash, 1, 48),   PlotStyle.Line, "Upper1");    // 1
                AddPlot(new Stroke(InnerBandBrush, DashStyleHelper.Dash, 1, 48),   PlotStyle.Line, "Lower1");    // 2
                AddPlot(new Stroke(OuterBandBrush, DashStyleHelper.Dot, 1, 30),    PlotStyle.Line, "Upper2");    // 3
                AddPlot(new Stroke(OuterBandBrush, DashStyleHelper.Dot, 1, 30),    PlotStyle.Line, "Lower2");    // 4
                AddPlot(new Stroke(ExtBrush,          1),                            PlotStyle.Dot,  "Extended");  // 5
            }
            else if (State == State.Configure)
            {
                anchorInt = ParseHhmmss(AnchorTime, 63000,  "AnchorTime");
                dayEndInt = ParseHhmmss(DayEndTime, 170000, "DayEndTime");
            }
            else if (State == State.DataLoaded)
            {
                // Lookback infinito: la nube (OnRender) lee cualquier barra visible,
                // no solo las ultimas 256. Cada barra recibe valor o NaN explicito.
                vwapSeries = new Series<double>(this, MaximumBarsLookBack.Infinite);
                cumPv = 0; cumVol = 0; cumPv2 = 0;
                anchored = false; sawIdleBar = false; prevBarAccum = false;
                currentDay = 0; barsToday = 0;
                lastCalculatedBar = -1;
                readoutFont = new SimpleFont("Segoe UI", FontSize);
                readoutSnapshot = null;
            }
            else if (State == State.Terminated)
            {
                DisposeCloudDx();
                DisposePanelDx();
            }
        }

        protected override void OnBarUpdate()
        {
            if (IsFirstTickOfBar)
            {
                // ---- Fold: integrate the just-CLOSED bar with its final values.
                if (CurrentBar > 0 && prevBarAccum)
                {
                    double vol1 = Volume[1];
                    if (vol1 > 0)
                    {
                        double tp1 = (High[1] + Low[1] + Close[1]) / 3.0;
                        cumPv += tp1 * vol1;
                        cumVol += vol1;
                        if (ShowBands)
                            cumPv2 += tp1 * tp1 * vol1;
                    }
                    if (cumVol > 0)
                    {
                        // Re-stamp the closed bar with the exact parity value.
                        double vw1 = cumPv / cumVol;
                        Values[0][1] = vw1;
                        vwapSeries[1] = vw1;
                        PlotBrushes[0][1] = Close[1] >= vw1 ? LineAboveBrush : LineBelowBrush;
                        if (ShowBands)
                        {
                            double var1 = (cumPv2 / cumVol) - (vw1 * vw1);
                            double sd1 = var1 > 0 ? Math.Sqrt(var1) : 0;
                            Values[1][1] = vw1 + Band1Mult * sd1;
                            Values[2][1] = vw1 - Band1Mult * sd1;
                            Values[3][1] = vw1 + Band2Mult * sd1;
                            Values[4][1] = vw1 - Band2Mult * sd1;
                        }
                        if (ShowExtendedMarker && ExtendedDistancePoints > 0
                            && Math.Abs(Close[1] - vw1) >= ExtendedDistancePoints)
                            Values[5][1] = vw1;
                        else
                            Values[5].Reset(1);
                    }
                }

                // ---- Partition/anchor state machine (mirror of the engine's daily reset).
                int day = ToDay(Time[0]);
                if (day != currentDay)
                {
                    currentDay = day;
                    anchored = false;
                    barsToday = 0;
                }
                barsToday++;

                int t = ToTime(Time[0]);
                bool live = t >= anchorInt && (dayEndInt <= anchorInt || t < dayEndInt);
                if (!live)
                {
                    anchored = false;
                    sawIdleBar = true;   // seen at least one idle bar -> next anchor is a FULL day
                }
                else if (!anchored && (sawIdleBar || t == anchorInt))
                {
                    // Also accept a chart whose very first loaded bar is exactly the
                    // anchor; in that case no pre-anchor idle bar exists to set sawIdleBar.
                    anchored = true;
                    cumPv = 0; cumVol = 0; cumPv2 = 0;
                }
                prevBarAccum = anchored;
            }

            // ---- Preview of the in-progress bar: NEVER mutates the accumulators.
            double vwap = 0, pv2 = 0, vol = 0;
            bool has = false;
            if (anchored)
            {
                double pv = cumPv;
                vol = cumVol;
                pv2 = cumPv2;
                double vol0 = Volume[0];
                if (vol0 > 0)
                {
                    double tp = (High[0] + Low[0] + Close[0]) / 3.0;
                    pv += tp * vol0;
                    vol += vol0;
                    if (ShowBands)
                        pv2 += tp * tp * vol0;
                }
                if (vol > 0)
                {
                    vwap = pv / vol;
                    has = true;
                }
            }

            if (!has)
            {
                for (int p = 0; p < 5; p++)
                    Values[p].Reset();
                vwapSeries[0] = double.NaN;
            }
            else
            {
                vwapSeries[0] = vwap;
                Values[0][0] = vwap;
                PlotBrushes[0][0] = Close[0] >= vwap ? LineAboveBrush : LineBelowBrush;
                if (ShowBands)
                {
                    double variance = (pv2 / vol) - (vwap * vwap);
                    double sd = variance > 0 ? Math.Sqrt(variance) : 0;
                    Values[1][0] = vwap + Band1Mult * sd;
                    Values[2][0] = vwap - Band1Mult * sd;
                    Values[3][0] = vwap + Band2Mult * sd;
                    Values[4][0] = vwap - Band2Mult * sd;
                }
                else
                {
                    Values[1].Reset(); Values[2].Reset();
                    Values[3].Reset(); Values[4].Reset();
                }
            }

            // ---- Extended marker + corner readout (manual aids, not parity).
            bool extended = has && ExtendedDistancePoints > 0
                            && Math.Abs(Close[0] - vwap) >= ExtendedDistancePoints;
            if (has && extended && ShowExtendedMarker)
                Values[5][0] = vwap;
            else
                Values[5].Reset();

            if (ShowReadout)
            {
                string text;
                if (!has)
                {
                    text = "VWAP  --\nSin valor para esta barra";
                    readoutSnapshot = new ReadoutSnapshot("VWAP  --", "SIN DATOS",
                        "--", "Cambio (" + SlopeLookbackBars + ")", "--", 0, false);
                }
                else
                {
                    double dist = Close[0] - vwap;
                    // Canonical engine slope: vwap[0] - vwap[lookback], capped by the
                    // bars available today (mirror of GetTrendMetadata / Python).
                    int barsAgo = Math.Min(Math.Min(Math.Max(0, SlopeLookbackBars),
                                                    Math.Max(0, barsToday - 1)), CurrentBar);
                    double prev = vwapSeries[barsAgo];
                    string slopeText = double.IsNaN(prev)
                        ? "n/a"
                        : (vwap - prev).ToString("+0.00;-0.00");
                    string side = dist > 0 ? "ARRIBA" : dist < 0 ? "DEBAJO" : "EN VWAP";
                    text = "VWAP  " + vwap.ToString("0.00") + "  |  " + side
                         + (extended ? "  |  EXTENDIDO" : "")
                         + "\nDist " + dist.ToString("+0.00;-0.00") + " pt"
                         + "    Cambio[" + SlopeLookbackBars + "] " + slopeText + " pt";
                    readoutSnapshot = new ReadoutSnapshot("VWAP  " + vwap.ToString("0.00"),
                        side, dist.ToString("+0.00;-0.00;0.00") + " pt",
                        "Cambio (" + SlopeLookbackBars + ")", slopeText + " pt",
                        dist > 0 ? 1 : dist < 0 ? -1 : 0, extended);
                }
                if (UseAlignedPanels)
                    RemoveDrawObject("dav3eVwap");
                else
                    Draw.TextFixed(this, "dav3eVwap", text, Position,
                        extended ? ExtBrush : ReadoutTextBrush,
                        readoutFont,
                        ShowReadoutPanel ? ReadoutBorderBrush : Brushes.Transparent,
                        ShowReadoutPanel ? ReadoutBackgroundBrush : Brushes.Transparent,
                        ShowReadoutPanel ? Math.Max(0, Math.Min(100, ReadoutOpacityPct)) : 0);
            }
            else
            {
                readoutSnapshot = null;
                RemoveDrawObject("dav3eVwap");
            }
            lastCalculatedBar = CurrentBar;
        }

        public override void OnRenderTargetChanged()
        {
            // Liberar los pinceles DX viejos y recrearlos ligados al RenderTarget
            // actual. NinjaTrader llama esto cuando el dispositivo grafico cambia.
            DisposeCloudDx();
            DisposePanelDx();
            if (RenderTarget == null)
                return;
            float a = Math.Max(0, Math.Min(40, CloudOpacityPct)) / 100f;
            cloudUpDx = (CloudAboveBrush ?? DefaultUpCloudBrush).ToDxBrush(RenderTarget, a);
            cloudDownDx = (CloudBelowBrush ?? DefaultDownCloudBrush).ToDxBrush(RenderTarget, a);
            panelBgDx = ReadoutBackgroundBrush.ToDxBrush(RenderTarget, 0.90f);
            panelBorderDx = ReadoutBorderBrush.ToDxBrush(RenderTarget, 0.80f);
            panelTextDx = ReadoutTextBrush.ToDxBrush(RenderTarget);
            panelLabelDx = CreateFrozen(148, 163, 184).ToDxBrush(RenderTarget);
            panelUpDx = CreateFrozen(54, 170, 144).ToDxBrush(RenderTarget);
            panelDownDx = CreateFrozen(224, 112, 130).ToDxBrush(RenderTarget);
            panelExtendedDx = ExtBrush.ToDxBrush(RenderTarget);
        }

        private void DisposeCloudDx()
        {
            if (cloudUpDx != null)   { cloudUpDx.Dispose();   cloudUpDx = null; }
            if (cloudDownDx != null) { cloudDownDx.Dispose(); cloudDownDx = null; }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            RenderCloud(chartControl, chartScale);
            base.OnRender(chartControl, chartScale);   // Lineas y bandas nitidas sobre el relleno.
            RenderAlignedReadout();
        }

        private void DisposePanelDx()
        {
            if (panelBgDx != null) { panelBgDx.Dispose(); panelBgDx = null; }
            if (panelBorderDx != null) { panelBorderDx.Dispose(); panelBorderDx = null; }
            if (panelTextDx != null) { panelTextDx.Dispose(); panelTextDx = null; }
            if (panelLabelDx != null) { panelLabelDx.Dispose(); panelLabelDx = null; }
            if (panelUpDx != null) { panelUpDx.Dispose(); panelUpDx = null; }
            if (panelDownDx != null) { panelDownDx.Dispose(); panelDownDx = null; }
            if (panelExtendedDx != null) { panelExtendedDx.Dispose(); panelExtendedDx = null; }
            if (panelTextFormat != null) { panelTextFormat.Dispose(); panelTextFormat = null; }
            if (panelTitleFormat != null) { panelTitleFormat.Dispose(); panelTitleFormat = null; }
            panelRenderScale = -1;
        }

        private void EnsurePanelFonts(float scale)
        {
            if (panelTextFormat != null && Math.Abs(panelRenderScale - scale) < 0.001f)
                return;
            if (panelTextFormat != null) panelTextFormat.Dispose();
            if (panelTitleFormat != null) panelTitleFormat.Dispose();
            panelTextFormat = new SharpDX.DirectWrite.TextFormat(
                NinjaTrader.Core.Globals.DirectWriteFactory, "Segoe UI",
                SharpDX.DirectWrite.FontWeight.Normal, SharpDX.DirectWrite.FontStyle.Normal, 12f * scale);
            panelTitleFormat = new SharpDX.DirectWrite.TextFormat(
                NinjaTrader.Core.Globals.DirectWriteFactory, "Segoe UI",
                SharpDX.DirectWrite.FontWeight.SemiBold, SharpDX.DirectWrite.FontStyle.Normal, 13f * scale);
            panelRenderScale = scale;
        }

        private void RenderAlignedReadout()
        {
            var snapshot = readoutSnapshot;
            if (!UseAlignedPanels || !ShowReadout || snapshot == null || RenderTarget == null
                || ChartPanel == null || panelBgDx == null || ChartPanel.W <= 32 || ChartPanel.H <= 32)
                return;

            // Mismo contrato que dav3e_hud: ancho 240, alturas 112/166 y hueco 16.
            // Se centra la pareja completa en el panel de precios, sin Chart Trader.
            float scale = Math.Min((float)PanelScale,
                Math.Min((ChartPanel.W - 16f) / 240f, (ChartPanel.H - 16f) / 294f));
            if (scale <= 0 || float.IsNaN(scale) || float.IsInfinity(scale)) return;
            float width = 240f * scale;
            float height = 112f * scale;
            float x = ChartPanel.X + Math.Max(8f,
                Math.Min(PanelLeftMargin * scale, ChartPanel.W - width - 8f));
            float y = ChartPanel.Y + (ChartPanel.H - 294f * scale) / 2f;
            EnsurePanelFonts(scale);

            if (ShowReadoutPanel)
            {
                var box = new SharpDX.Direct2D1.RoundedRectangle
                {
                    Rect = new SharpDX.RectangleF(x, y, width, height),
                    RadiusX = 6f * scale, RadiusY = 6f * scale
                };
                RenderTarget.FillRoundedRectangle(box, panelBgDx);
                RenderTarget.DrawRoundedRectangle(box, panelBorderDx, Math.Max(0.5f, scale));
                RenderTarget.DrawLine(new SharpDX.Vector2(x + 14f * scale, y + 34f * scale),
                    new SharpDX.Vector2(x + width - 14f * scale, y + 34f * scale),
                    panelBorderDx, Math.Max(0.5f, scale));
            }

            DrawPanelText(snapshot.Title, panelTitleFormat, panelTextDx,
                x + 14f * scale, y, width - 28f * scale, 34f * scale,
                SharpDX.DirectWrite.TextAlignment.Center);

            var sideBrush = snapshot.SideSign > 0 ? panelUpDx
                : snapshot.SideSign < 0 ? panelDownDx : panelLabelDx;
            // Solo el aviso EXTENDIDO usa ambar; el resto conserva su jerarquia.
            using (var side = new SharpDX.DirectWrite.TextLayout(
                NinjaTrader.Core.Globals.DirectWriteFactory, snapshot.Side, panelTextFormat, width, 20f * scale))
            using (var extended = new SharpDX.DirectWrite.TextLayout(
                NinjaTrader.Core.Globals.DirectWriteFactory, snapshot.Extended ? "EXTENDIDO" : "",
                panelTextFormat, width, 20f * scale))
            {
                side.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;
                extended.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;
                side.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
                extended.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
                float statusWidth = side.Metrics.Width
                    + (snapshot.Extended ? 12f * scale + extended.Metrics.Width : 0);
                float statusX = x + (width - statusWidth) / 2f;
                RenderTarget.DrawTextLayout(new SharpDX.Vector2(statusX, y + 38f * scale), side, sideBrush);
                if (snapshot.Extended)
                    RenderTarget.DrawTextLayout(new SharpDX.Vector2(statusX + side.Metrics.Width + 12f * scale,
                        y + 38f * scale), extended, panelExtendedDx);
            }

            float contentX = x + 14f * scale;
            float contentWidth = width - 28f * scale;
            DrawPanelText("Distancia", panelTextFormat, panelLabelDx,
                contentX, y + 62f * scale, contentWidth / 2f, 20f * scale);
            DrawPanelText(snapshot.Distance, panelTextFormat, sideBrush,
                contentX + contentWidth / 2f, y + 62f * scale, contentWidth / 2f, 20f * scale,
                SharpDX.DirectWrite.TextAlignment.Trailing);
            DrawPanelText(snapshot.ChangeLabel, panelTextFormat, panelLabelDx,
                contentX, y + 84f * scale, contentWidth / 2f, 20f * scale);
            DrawPanelText(snapshot.Change, panelTextFormat, panelTextDx,
                contentX + contentWidth / 2f, y + 84f * scale, contentWidth / 2f, 20f * scale,
                SharpDX.DirectWrite.TextAlignment.Trailing);
        }

        private void DrawPanelText(string text, SharpDX.DirectWrite.TextFormat format,
            SharpDX.Direct2D1.Brush brush, float x, float y, float width, float height,
            SharpDX.DirectWrite.TextAlignment alignment = SharpDX.DirectWrite.TextAlignment.Leading)
        {
            using (var layout = new SharpDX.DirectWrite.TextLayout(
                NinjaTrader.Core.Globals.DirectWriteFactory, text, format, width, height))
            {
                layout.TextAlignment = alignment;
                layout.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
                layout.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;
                RenderTarget.DrawTextLayout(new SharpDX.Vector2(x, y), layout, brush,
                    SharpDX.Direct2D1.DrawTextOptions.Clip);
            }
        }

        private void RenderCloud(ChartControl chartControl, ChartScale chartScale)
        {
            if (!ShowCloud || CloudOpacityPct <= 0 || RenderTarget == null || ChartBars == null
                || vwapSeries == null || cloudUpDx == null || cloudDownDx == null)
                return;

            int from = Math.Max(ChartBars.FromIndex, 0);
            int to = Math.Min(Math.Min(ChartBars.ToIndex, Bars.Count - 1),
                              Math.Min(lastCalculatedBar, vwapSeries.Count - 1));

            // Nube = banda rellena entre el precio (Close) y el VWAP, por tramos
            // de un mismo lado (teal cuando el precio esta por encima del valor,
            // rojo por debajo). Solo barras visibles y con VWAP valido.
            var vwapPts  = new System.Collections.Generic.List<SharpDX.Vector2>();
            var pricePts = new System.Collections.Generic.List<SharpDX.Vector2>();
            bool runUp = false;
            float prevX = 0, prevYv = 0, prevYp = 0;
            bool havePrev = false;
            int prevDay = 0;

            for (int i = from; i <= to; i++)
            {
                double v = vwapSeries.IsValidDataPointAt(i)
                    ? vwapSeries.GetValueAt(i) : double.NaN;
                double price = Close.GetValueAt(i);
                bool valid = !double.IsNaN(v) && !double.IsInfinity(v)
                    && !double.IsNaN(price) && !double.IsInfinity(price);
                if (!valid)
                {
                    FlushCloudRun(vwapPts, pricePts, runUp);
                    vwapPts.Clear(); pricePts.Clear(); havePrev = false;
                    continue;
                }

                int day = ToDay(Time.GetValueAt(i));
                if (havePrev && day != prevDay)
                {
                    // No unir con una nube dos anclajes de fechas distintas.
                    FlushCloudRun(vwapPts, pricePts, runUp);
                    vwapPts.Clear(); pricePts.Clear(); havePrev = false;
                }
                float x  = chartControl.GetXByBarIndex(ChartBars, i);
                float yv = chartScale.GetYByValue(v);
                float yp = chartScale.GetYByValue(price);
                bool up  = price >= v;

                if (vwapPts.Count > 0 && up != runUp)
                {
                    // Dividir en el cruce real de las dos lineas dibujadas.
                    double previousGap = (double)prevYp - prevYv;
                    double currentGap = (double)yp - yv;
                    double denominator = previousGap - currentGap;
                    double fraction = denominator == 0 ? 0 : previousGap / denominator;
                    fraction = Math.Max(0, Math.Min(1, fraction));
                    var crossing = new SharpDX.Vector2(
                        (float)(prevX + (x - prevX) * fraction),
                        (float)(prevYv + (yv - prevYv) * fraction));
                    vwapPts.Add(crossing); pricePts.Add(crossing);
                    FlushCloudRun(vwapPts, pricePts, runUp);
                    vwapPts.Clear(); pricePts.Clear();
                    vwapPts.Add(crossing); pricePts.Add(crossing);
                    runUp = up;
                }

                if (vwapPts.Count == 0)
                    runUp = up;
                vwapPts.Add(new SharpDX.Vector2(x, yv));
                pricePts.Add(new SharpDX.Vector2(x, yp));
                prevX = x; prevYv = yv; prevYp = yp; havePrev = true;
                prevDay = day;
            }
            FlushCloudRun(vwapPts, pricePts, runUp);
        }

        private void FlushCloudRun(System.Collections.Generic.List<SharpDX.Vector2> vwapPts,
                                   System.Collections.Generic.List<SharpDX.Vector2> pricePts, bool up)
        {
            if (vwapPts.Count < 2)
                return;
            using (var geo = new SharpDX.Direct2D1.PathGeometry(RenderTarget.Factory))
            using (var sink = geo.Open())
            {
                sink.BeginFigure(vwapPts[0], SharpDX.Direct2D1.FigureBegin.Filled);
                for (int i = 1; i < vwapPts.Count; i++)
                    sink.AddLine(vwapPts[i]);
                for (int i = pricePts.Count - 1; i >= 0; i--)
                    sink.AddLine(pricePts[i]);
                sink.EndFigure(SharpDX.Direct2D1.FigureEnd.Closed);
                sink.Close();
                RenderTarget.FillGeometry(geo, up ? cloudUpDx : cloudDownDx);
            }
        }

        private int ParseHhmmss(string value, int fallback, string label)
        {
            string padded = (value ?? string.Empty).Trim().PadLeft(6, '0');
            bool ok = padded.Length == 6;
            if (ok)
                for (int i = 0; i < 6; i++)
                    if (!char.IsDigit(padded[i])) { ok = false; break; }
            if (ok)
            {
                int hh = int.Parse(padded.Substring(0, 2));
                int mm = int.Parse(padded.Substring(2, 2));
                int ss = int.Parse(padded.Substring(4, 2));
                if (hh <= 23 && mm <= 59 && ss <= 59)
                    return hh * 10000 + mm * 100 + ss;
            }
            Print(Name + ": " + label + " invalido ('" + value + "'); usando " + fallback.ToString("D6") + ".");
            return fallback;
        }

        [NinjaScriptProperty]
        [Display(Name = "Anchor time (HHmmss, chart time)", Order = 1, GroupName = "Parameters")]
        public string AnchorTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show sigma bands", Order = 2, GroupName = "Parameters")]
        public bool ShowBands { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10)]
        [Display(Name = "Band 1 multiple", Order = 3, GroupName = "Parameters")]
        public double Band1Mult { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10)]
        [Display(Name = "Band 2 multiple", Order = 4, GroupName = "Parameters")]
        public double Band2Mult { get; set; }

        [Display(Name = "Day end time (HHmmss, chart time)", Order = 5, GroupName = "Parameters")]
        public string DayEndTime { get; set; }

        [Display(Name = "Show corner readout", Order = 6, GroupName = "Parameters")]
        public bool ShowReadout { get; set; }

        [Display(Name = "Readout position", Order = 7, GroupName = "Parameters")]
        public TextPosition Position { get; set; }

        [Range(6, 30)]
        [Display(Name = "Font size", Order = 8, GroupName = "Parameters")]
        public int FontSize { get; set; }

        [Range(1, 500)]
        [Display(Name = "Slope lookback bars", Order = 9, GroupName = "Parameters")]
        public int SlopeLookbackBars { get; set; }

        [Range(0, 100)]
        [Display(Name = "Extended distance (points, 0 = off)", Order = 10, GroupName = "Parameters")]
        public double ExtendedDistancePoints { get; set; }

        [Display(Name = "Show extended marker", Order = 11, GroupName = "Parameters")]
        public bool ShowExtendedMarker { get; set; }

        [Display(Name = "Show price/VWAP cloud", Order = 12, GroupName = "Parameters")]
        public bool ShowCloud { get; set; }

        [Range(0, 40)]
        [Display(Name = "Cloud opacity (%)", Order = 13, GroupName = "Parameters")]
        public int CloudOpacityPct { get; set; }

        [XmlIgnore]
        [Display(Name = "Linea: precio arriba", Order = 1, GroupName = "Apariencia")]
        public Brush LineAboveBrush { get; set; }

        [Browsable(false)]
        public string LineAboveBrushSerialize
        {
            get { return Serialize.BrushToString(LineAboveBrush); }
            set { LineAboveBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Linea: precio debajo", Order = 2, GroupName = "Apariencia")]
        public Brush LineBelowBrush { get; set; }

        [Browsable(false)]
        public string LineBelowBrushSerialize
        {
            get { return Serialize.BrushToString(LineBelowBrush); }
            set { LineBelowBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Nube: precio arriba", Order = 3, GroupName = "Apariencia")]
        public Brush CloudAboveBrush { get; set; }

        [Browsable(false)]
        public string CloudAboveBrushSerialize
        {
            get { return Serialize.BrushToString(CloudAboveBrush); }
            set { CloudAboveBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Nube: precio debajo", Order = 4, GroupName = "Apariencia")]
        public Brush CloudBelowBrush { get; set; }

        [Browsable(false)]
        public string CloudBelowBrushSerialize
        {
            get { return Serialize.BrushToString(CloudBelowBrush); }
            set { CloudBelowBrush = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Fondo del panel VWAP", Order = 5, GroupName = "Apariencia")]
        public bool ShowReadoutPanel { get; set; }

        [Range(0, 100)]
        [Display(Name = "Opacidad del panel (%)", Order = 6, GroupName = "Apariencia")]
        public int ReadoutOpacityPct { get; set; }

        [Display(Name = "Alinear paneles a la izquierda", Order = 1, GroupName = "Paneles")]
        [Description("Centra verticalmente la pareja VWAP/HUD. Desactiva para usar Position y Font size.")]
        public bool UseAlignedPanels { get; set; }

        [Range(0.75, 2.0)]
        [Display(Name = "Escala de los paneles", Order = 2, GroupName = "Paneles")]
        [Description("Usa la misma escala en dav3e_vwap y dav3e_hud para conservar la alineacion.")]
        public double PanelScale { get; set; }

        [Range(0, 400)]
        [Display(Name = "Margen izquierdo (px)", Order = 3, GroupName = "Paneles")]
        [Description("Usa el mismo margen en dav3e_vwap y dav3e_hud. Se ajusta a la escala del panel.")]
        public int PanelLeftMargin { get; set; }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private dav3e_vwap[] cachedav3e_vwap;
		public dav3e_vwap dav3e_vwap(string anchorTime, bool showBands, double band1Mult, double band2Mult)
		{
			return dav3e_vwap(Input, anchorTime, showBands, band1Mult, band2Mult);
		}

		public dav3e_vwap dav3e_vwap(ISeries<double> input, string anchorTime, bool showBands, double band1Mult, double band2Mult)
		{
			if (cachedav3e_vwap != null)
				for (int idx = 0; idx < cachedav3e_vwap.Length; idx++)
					if (cachedav3e_vwap[idx] != null && cachedav3e_vwap[idx].AnchorTime == anchorTime && cachedav3e_vwap[idx].ShowBands == showBands && cachedav3e_vwap[idx].Band1Mult == band1Mult && cachedav3e_vwap[idx].Band2Mult == band2Mult && cachedav3e_vwap[idx].EqualsInput(input))
						return cachedav3e_vwap[idx];
			return CacheIndicator<dav3e_vwap>(new dav3e_vwap(){ AnchorTime = anchorTime, ShowBands = showBands, Band1Mult = band1Mult, Band2Mult = band2Mult }, input, ref cachedav3e_vwap);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.dav3e_vwap dav3e_vwap(string anchorTime, bool showBands, double band1Mult, double band2Mult)
		{
			return indicator.dav3e_vwap(Input, anchorTime, showBands, band1Mult, band2Mult);
		}

		public Indicators.dav3e_vwap dav3e_vwap(ISeries<double> input , string anchorTime, bool showBands, double band1Mult, double band2Mult)
		{
			return indicator.dav3e_vwap(input, anchorTime, showBands, band1Mult, band2Mult);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.dav3e_vwap dav3e_vwap(string anchorTime, bool showBands, double band1Mult, double band2Mult)
		{
			return indicator.dav3e_vwap(Input, anchorTime, showBands, band1Mult, band2Mult);
		}

		public Indicators.dav3e_vwap dav3e_vwap(ISeries<double> input , string anchorTime, bool showBands, double band1Mult, double band2Mult)
		{
			return indicator.dav3e_vwap(input, anchorTime, showBands, band1Mult, band2Mult);
		}
	}
}

#endregion
