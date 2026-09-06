#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;   // TextPosition vive aqui
#endregion

// =============================================================================
// dav3e_hud  --  NinjaTrader 8 indicator
//
// Panel unico (HUD) con los numeros operativos de la suite Dav3e, dibujado con
// OnRender: fondo oscuro semitransparente y filas coloreadas.
//
//   DELTA  +312  r 0.014   delta BidAsk del minuto EN CURSO (paridad dav3e_delta:
//                          >= ask compra, <= bid venta, dentro del spread se
//                          descarta) + ratio delta/volumen -- los DOS numeros
//                          que filtran las entradas del sistema (min 3 / 0.005)
//   D PREV +188            delta del minuto anterior (cerrado)
//   D SES  +1,204          delta acumulado de la sesion
//   ATR    2.15            Wilder continuo (paridad dav3e_atr / analyzer)
//   TICKS  431 / 1,204     ticks del bar en curso / del bar anterior
//
// El delta/ticks viven del feed (OnMarketData): en VIVO funcionan siempre; en
// historico solo con Tick Replay (sin el, muestran "--" hasta el primer tick).
// Pensado para charts de 1 minuto (los buckets de delta son por minuto).
// Sustituye a los readouts de esquina de dav3e_atr y dav3e_ticks si lo usas.
// =============================================================================

namespace NinjaTrader.NinjaScript.Indicators
{
    public class dav3e_hud : Indicator
    {
        // --- delta / ticks / volumen del minuto en curso (rollover por bucket) ---
        private double lastBid, lastAsk;
        private string liveKey;
        private long   liveDelta, prevDelta, sessionDelta;
        private long   liveTicks, prevTicks;
        private double liveVol;
        private bool   sawTick;
        private readonly object sync = new object();

        // --- ATR Wilder continuo (fold de barras cerradas) ---
        private double atr, prevCloseAtr, trSum;
        private int    trCount;

        // --- Recursos Direct2D/DirectWrite ---
        private SharpDX.Direct2D1.SolidColorBrush bgDx, borderDx, labelDx, valueDx, upDx, downDx, dimDx;
        private SharpDX.DirectWrite.TextFormat textFormat, titleFormat;
        private float lastTextSize, lastTitleSize;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "dav3e_hud";
                Description = "dav3e HUD: delta vivo + ratio, delta de sesion, ATR y frecuencia de ticks en un panel unico.";
                IsOverlay   = true;
                // Delta/tick counters would lose OnMarketData events while a tab
                // or workspace is in the background.
                IsSuspendedWhileInactive = false;
                Calculate   = Calculate.OnEachTick;
                Position    = TextPosition.TopRight;   // sustituye a dav3e_atr (esa esquina)
                FontSize    = 12;
                UseAlignedPanels = true;
                PanelScale = 1.0;
                PanelLeftMargin = 20;
                AtrPeriod   = 14;
                ShowDelta        = true;
                ShowSessionDelta = true;
                ShowAtr          = true;
                ShowTicks        = true;
            }
            else if (State == State.DataLoaded)
            {
                lastBid = 0; lastAsk = 0;
                liveKey = null; liveDelta = 0; prevDelta = 0; sessionDelta = 0;
                liveTicks = 0; prevTicks = 0; liveVol = 0; sawTick = false;
                atr = 0; prevCloseAtr = 0; trSum = 0; trCount = 0;
            }
            else if (State == State.Terminated)
            {
                DisposeDx();
            }
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            lock (sync)
            {
                if (e.MarketDataType == MarketDataType.Bid) { lastBid = e.Price; return; }
                if (e.MarketDataType == MarketDataType.Ask) { lastAsk = e.Price; return; }
                if (e.MarketDataType != MarketDataType.Last) return;

                // Bucket por minuto +1 (paridad dav3e_delta): el tick del minuto MM
                // alimenta la barra que CIERRA en MM+1.
                string key = e.Time.AddMinutes(1).ToString("yyyyMMddHHmm");
                if (key != liveKey)
                {
                    if (liveKey != null)
                    {
                        prevDelta = liveDelta;
                        prevTicks = liveTicks;
                        sessionDelta += liveDelta;
                    }
                    liveKey = key;
                    liveDelta = 0; liveTicks = 0; liveVol = 0;
                }

                // Tick Replay only emits Last events; the historical bid/ask
                // snapshot is carried by that event.
                double bid = e.Bid > 0 ? e.Bid : lastBid > 0 ? lastBid : e.Price;
                double ask = e.Ask > 0 ? e.Ask : lastAsk > 0 ? lastAsk : e.Price;
                if (e.Price >= ask)      liveDelta += (long)e.Volume;
                else if (e.Price <= bid) liveDelta -= (long)e.Volume;
                // dentro del spread -> descartado para el delta (paridad Python)

                liveTicks++;
                liveVol += e.Volume;
                sawTick = true;
            }
        }

        protected override void OnBarUpdate()
        {
            lock (sync)
            {
                // OnBarUpdate precedes OnMarketData, so this reset happens before
                // the first trade of the new session is accumulated.
                if (IsFirstTickOfBar && Bars.IsFirstBarOfSession)
                {
                    lastBid = 0; lastAsk = 0;
                    liveKey = null; liveDelta = 0; prevDelta = 0; sessionDelta = 0;
                    liveTicks = 0; prevTicks = 0; liveVol = 0; sawTick = false;
                }

                // ATR Wilder continuo, integrando solo barras CERRADAS (indice 1).
                if (IsFirstTickOfBar && CurrentBar > 0)
                {
                    double tr = CurrentBar == 1
                        ? Math.Max(0.0, High[1] - Low[1])
                        : Math.Max(High[1] - Low[1],
                          Math.Max(Math.Abs(High[1] - prevCloseAtr), Math.Abs(Low[1] - prevCloseAtr)));
                    trCount++;
                    if (trCount <= AtrPeriod)
                    {
                        trSum += tr;
                        atr = trSum / trCount;                         // media -> SMA al llegar al periodo
                    }
                    else
                    {
                        atr = (atr * (AtrPeriod - 1) + tr) / AtrPeriod; // suavizado Wilder
                    }
                    prevCloseAtr = Close[1];
                }
            }
        }

        public override void OnRenderTargetChanged()
        {
            DisposeDx();
            if (RenderTarget == null)
                return;
            bgDx     = NewDx(17, 24, 34, 0.90f);
            borderDx = NewDx(53, 67, 86, 0.80f);
            labelDx  = NewDx(148, 163, 184, 1f);
            valueDx  = NewDx(222, 230, 242, 1f);
            upDx     = NewDx(54, 170, 144, 1f);
            downDx   = NewDx(224, 112, 130, 1f);
            dimDx    = NewDx(148, 163, 184, 1f);
        }

        private SharpDX.Direct2D1.SolidColorBrush NewDx(byte r, byte g, byte b, float a)
        {
            return new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,
                new SharpDX.Color4(r / 255f, g / 255f, b / 255f, a));
        }

        private void DisposeDx()
        {
            if (bgDx != null)     { bgDx.Dispose();     bgDx = null; }
            if (borderDx != null) { borderDx.Dispose(); borderDx = null; }
            if (labelDx != null)  { labelDx.Dispose();  labelDx = null; }
            if (valueDx != null)  { valueDx.Dispose();  valueDx = null; }
            if (upDx != null)     { upDx.Dispose();     upDx = null; }
            if (downDx != null)   { downDx.Dispose();   downDx = null; }
            if (dimDx != null)    { dimDx.Dispose();    dimDx = null; }
            if (textFormat != null) { textFormat.Dispose(); textFormat = null; }
            if (titleFormat != null) { titleFormat.Dispose(); titleFormat = null; }
            lastTextSize = 0; lastTitleSize = 0;
        }

        private void EnsurePanelFonts(float scale)
        {
            float textSize = 12f * scale;
            float titleSize = 13f * scale;
            if (textFormat != null && titleFormat != null
                && lastTextSize == textSize && lastTitleSize == titleSize)
                return;

            if (textFormat != null) textFormat.Dispose();
            if (titleFormat != null) titleFormat.Dispose();
            textFormat = new SharpDX.DirectWrite.TextFormat(
                NinjaTrader.Core.Globals.DirectWriteFactory, "Segoe UI",
                SharpDX.DirectWrite.FontWeight.Normal,
                SharpDX.DirectWrite.FontStyle.Normal, textSize);
            titleFormat = new SharpDX.DirectWrite.TextFormat(
                NinjaTrader.Core.Globals.DirectWriteFactory, "Segoe UI",
                SharpDX.DirectWrite.FontWeight.SemiBold,
                SharpDX.DirectWrite.FontStyle.Normal, titleSize);
            textFormat.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;
            titleFormat.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;
            lastTextSize = textSize;
            lastTitleSize = titleSize;
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            if (RenderTarget == null || bgDx == null || borderDx == null || ChartPanel == null
                || ChartPanel.W <= 32 || ChartPanel.H <= 32)
                return;

            // Contrato visual compartido con dav3e_vwap: 240 x (112 + 16 + 166).
            // La escala se ajusta al panel para que ambas tarjetas quepan juntas.
            float referenceHeight = UseAlignedPanels ? 294f : 166f;
            double requestedScale = UseAlignedPanels ? PanelScale : PanelScale * FontSize / 12.0;
            float scale = (float)Math.Min(requestedScale,
                Math.Min((ChartPanel.W - 16) / 240.0, (ChartPanel.H - 16) / referenceHeight));
            if (scale <= 0 || float.IsNaN(scale) || float.IsInfinity(scale))
                return;
            EnsurePanelFonts(scale);

            long liveDeltaNow, prevDeltaNow, sessionDeltaNow, liveTicksNow, prevTicksNow;
            double liveVolNow, atrNow;
            bool sawTickNow, atrReady;
            lock (sync)
            {
                liveDeltaNow = liveDelta;
                prevDeltaNow = prevDelta;
                sessionDeltaNow = sessionDelta + liveDelta;
                liveTicksNow = liveTicks;
                prevTicksNow = prevTicks;
                liveVolNow = liveVol;
                atrNow = atr;
                sawTickNow = sawTick;
                atrReady = trCount > 0;
            }

            // ---- Filas: (etiqueta, valor, pincel del valor) ----
            var rows = new List<Tuple<string, string, SharpDX.Direct2D1.SolidColorBrush>>();
            if (ShowDelta)
            {
                if (!sawTickNow)
                {
                    rows.Add(Tuple.Create("DELTA", "--", dimDx));
                    rows.Add(Tuple.Create("D PREV", "--", dimDx));
                }
                else
                {
                    double ratio = liveVolNow > 0 ? liveDeltaNow / liveVolNow : 0;
                    string v = liveDeltaNow.ToString("+#,##0;-#,##0;0", CultureInfo.InvariantCulture)
                             + "  r " + Math.Abs(ratio).ToString("0.000", CultureInfo.InvariantCulture);
                    rows.Add(Tuple.Create("DELTA", v,
                        liveDeltaNow > 0 ? upDx : liveDeltaNow < 0 ? downDx : dimDx));
                    rows.Add(Tuple.Create("D PREV",
                        prevDeltaNow.ToString("+#,##0;-#,##0;0", CultureInfo.InvariantCulture),
                        prevDeltaNow > 0 ? upDx : prevDeltaNow < 0 ? downDx : dimDx));
                }
            }
            if (ShowSessionDelta)
            {
                rows.Add(Tuple.Create("D SES",
                    sawTickNow ? sessionDeltaNow.ToString("+#,##0;-#,##0;0", CultureInfo.InvariantCulture) : "--",
                    !sawTickNow ? dimDx : sessionDeltaNow > 0 ? upDx : sessionDeltaNow < 0 ? downDx : dimDx));
            }
            if (ShowAtr)
                rows.Add(Tuple.Create("ATR", atrReady
                    ? atrNow.ToString("0.00", CultureInfo.InvariantCulture) : "--", atrReady ? valueDx : dimDx));
            if (ShowTicks)
            {
                string v = sawTickNow
                    ? liveTicksNow.ToString("#,##0", CultureInfo.InvariantCulture)
                      + " / " + prevTicksNow.ToString("#,##0", CultureInfo.InvariantCulture)
                    : "--";
                rows.Add(Tuple.Create("TICKS", v, sawTickNow ? valueDx : dimDx));
            }
            if (rows.Count == 0)
                return;

            float boxW = 240f * scale, boxH = 166f * scale;
            float x, y;
            if (UseAlignedPanels)
            {
                x = ChartPanel.X + (float)Math.Max(8,
                    Math.Min(PanelLeftMargin * scale, ChartPanel.W - boxW - 8));
                float yStack = ChartPanel.Y + (ChartPanel.H - 294f * scale) / 2f;
                y = yStack + 128f * scale;
            }
            else
            {
                // Compatibilidad con la esquina seleccionada en plantillas previas.
                x = ChartPanel.X + 10;
                y = ChartPanel.Y + 10;
                bool right = Position == TextPosition.TopRight || Position == TextPosition.BottomRight;
                bool bottom = Position == TextPosition.BottomLeft || Position == TextPosition.BottomRight;
                if (right)  x = ChartPanel.X + ChartPanel.W - boxW - 10;
                if (bottom) y = ChartPanel.Y + ChartPanel.H - boxH - 10;
                if (Position == TextPosition.Center)
                {
                    x = ChartPanel.X + (ChartPanel.W - boxW) / 2f;
                    y = ChartPanel.Y + (ChartPanel.H - boxH) / 2f;
                }
            }

            var rounded = new SharpDX.Direct2D1.RoundedRectangle
            {
                Rect = new SharpDX.RectangleF(x, y, boxW, boxH),
                RadiusX = 6f * scale,
                RadiusY = 6f * scale
            };
            RenderTarget.FillRoundedRectangle(rounded, bgDx);
            RenderTarget.DrawRoundedRectangle(rounded, borderDx, Math.Max(0.5f, scale));
            RenderTarget.DrawLine(new SharpDX.Vector2(x + 14f * scale, y + 34f * scale),
                new SharpDX.Vector2(x + boxW - 14f * scale, y + 34f * scale),
                borderDx, Math.Max(0.5f, scale));

            DrawPanelText("FLUJO Y VOLATILIDAD", titleFormat,
                x + 14f * scale, y, boxW - 28f * scale, 34f * scale,
                valueDx, SharpDX.DirectWrite.TextAlignment.Center);

            float rowY = y + 42f * scale;
            foreach (var row in rows)
            {
                DrawPanelText(row.Item1, textFormat,
                    x + 14f * scale, rowY, 60f * scale, 22f * scale,
                    labelDx, SharpDX.DirectWrite.TextAlignment.Leading);
                DrawPanelText(row.Item2, textFormat,
                    x + 80f * scale, rowY, 146f * scale, 22f * scale,
                    row.Item3, SharpDX.DirectWrite.TextAlignment.Trailing);
                rowY += 22f * scale;
            }
        }

        private void DrawPanelText(string text, SharpDX.DirectWrite.TextFormat format,
            float x, float y, float width, float height,
            SharpDX.Direct2D1.SolidColorBrush brush, SharpDX.DirectWrite.TextAlignment alignment)
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

        [Display(Name = "Alinear paneles a la izquierda", Order = 1, GroupName = "Paneles",
            Description = "Coloca el HUD debajo del VWAP a la izquierda. Activa esta opcion en ambos indicadores para alinear la pareja; Position solo se usa al desactivarla.")]
        public bool UseAlignedPanels { get; set; }

        [Range(0.75, 2.0)]
        [Display(Name = "Escala de los paneles", Order = 2, GroupName = "Paneles",
            Description = "Usa el mismo valor en dav3e_vwap y dav3e_hud para ajustar ambos paneles juntos. En modo alineado determina tambien el tamano de letra.")]
        public double PanelScale { get; set; }

        [Range(0, 400)]
        [Display(Name = "Margen izquierdo (px)", Order = 3, GroupName = "Paneles",
            Description = "Usa el mismo margen en dav3e_vwap y dav3e_hud para conservar su alineacion horizontal.")]
        public int PanelLeftMargin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Position", Order = 1, GroupName = "Parameters")]
        public TextPosition Position { get; set; }

        [NinjaScriptProperty]
        [Range(8, 30)]
        [Display(Name = "Font size", Order = 2, GroupName = "Parameters")]
        public int FontSize { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ATR period", Order = 3, GroupName = "Parameters")]
        public int AtrPeriod { get; set; }

        [Display(Name = "Show delta rows", Order = 4, GroupName = "Parameters")]
        public bool ShowDelta { get; set; }

        [Display(Name = "Show session delta", Order = 5, GroupName = "Parameters")]
        public bool ShowSessionDelta { get; set; }

        [Display(Name = "Show ATR", Order = 6, GroupName = "Parameters")]
        public bool ShowAtr { get; set; }

        [Display(Name = "Show ticks", Order = 7, GroupName = "Parameters")]
        public bool ShowTicks { get; set; }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private dav3e_hud[] cachedav3e_hud;
		public dav3e_hud dav3e_hud(TextPosition position, int fontSize, int atrPeriod)
		{
			return dav3e_hud(Input, position, fontSize, atrPeriod);
		}

		public dav3e_hud dav3e_hud(ISeries<double> input, TextPosition position, int fontSize, int atrPeriod)
		{
			if (cachedav3e_hud != null)
				for (int idx = 0; idx < cachedav3e_hud.Length; idx++)
					if (cachedav3e_hud[idx] != null && cachedav3e_hud[idx].Position == position && cachedav3e_hud[idx].FontSize == fontSize && cachedav3e_hud[idx].AtrPeriod == atrPeriod && cachedav3e_hud[idx].EqualsInput(input))
						return cachedav3e_hud[idx];
			return CacheIndicator<dav3e_hud>(new dav3e_hud(){ Position = position, FontSize = fontSize, AtrPeriod = atrPeriod }, input, ref cachedav3e_hud);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.dav3e_hud dav3e_hud(TextPosition position, int fontSize, int atrPeriod)
		{
			return indicator.dav3e_hud(Input, position, fontSize, atrPeriod);
		}

		public Indicators.dav3e_hud dav3e_hud(ISeries<double> input , TextPosition position, int fontSize, int atrPeriod)
		{
			return indicator.dav3e_hud(input, position, fontSize, atrPeriod);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.dav3e_hud dav3e_hud(TextPosition position, int fontSize, int atrPeriod)
		{
			return indicator.dav3e_hud(Input, position, fontSize, atrPeriod);
		}

		public Indicators.dav3e_hud dav3e_hud(ISeries<double> input , TextPosition position, int fontSize, int atrPeriod)
		{
			return indicator.dav3e_hud(input, position, fontSize, atrPeriod);
		}
	}
}

#endregion
