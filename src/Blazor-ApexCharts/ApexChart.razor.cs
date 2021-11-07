﻿using BlazorApexCharts;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ApexCharts
{
    public partial class ApexChart<TItem> : IDisposable where TItem : class
    {
        [Inject] public IJSRuntime JSRuntime { get; set; }
        [Parameter] public RenderFragment ChildContent { get; set; }
        [Parameter] public ApexChartOptions<TItem> Options { get; set; } = new ApexChartOptions<TItem>();
        [Parameter] public string Title { get; set; }

        [Parameter] public XAxisType? XAxisType { get; set; }
        [Parameter] public bool Debug { get; set; }
        [Parameter] public object Width { get; set; }
        [Parameter] public object Height { get; set; }

        [Parameter] public EventCallback<SelectedData<TItem>> OnDataPointSelection { get; set; }

        private DotNetObjectReference<ApexChart<TItem>> ObjectReference;
        private ElementReference ChartContainer { get; set; }

        private List<IApexSeries<TItem>> apexSeries = new();

        private bool isReady;
        private bool forceRender = true;


        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                isReady = true;
                ObjectReference = DotNetObjectReference.Create(this);
            }

            if (isReady && forceRender)
            {
                await Render();
            }
        }

        protected override void OnParametersSet()
        {
            if (Options.Chart == null) { Options.Chart = new Chart(); }

            Options.Debug = Debug;
            Options.Chart.Width = Width;
            Options.Chart.Height = Height;

            if (XAxisType != null)
            {
                if (Options.Xaxis == null) { Options.Xaxis = new XAxis(); }
                Options.Xaxis.Type = XAxisType;
            }

            if (string.IsNullOrEmpty(Title))
            {
                Options.Title = null;
            }
            else
            {
                if (Options.Title == null) { Options.Title = new Title(); }
                Options.Title.Text = Title;
            }
        }

        internal void AddSeries(IApexSeries<TItem> series)
        {
            if (!apexSeries.Contains(series))
            {
                apexSeries.Add(series);
            }

        }

        internal void RemoveSeries(IApexSeries<TItem> series)
        {
            if (apexSeries.Contains(series))
            {
                apexSeries.Remove(series);
            }
        }

        private bool IsNoAxisChart
        {
            get
            {
                return Options?.Chart?.Type == ChartType.Pie ||
               Options?.Chart?.Type == ChartType.Donut ||
               Options?.Chart?.Type == ChartType.PolarArea ||
               Options?.Chart?.Type == ChartType.RadialBar;
            }
        }

        private void SetStroke()
        {
            if (Options?.Series == null) { return; }
            if (apexSeries.All(e => (e.Stroke == null))) { return; }

            if (Options.Stroke == null) { Options.Stroke = new Stroke(); }

            var strokeWidths = new List<int>();
            var strokeColors = new List<string>();
            var strokeDash = new List<int>();
            foreach (var series in Options.Series)
            {
                strokeWidths.Add(series.ApexSeries.Stroke?.Width ?? 4); // 
                strokeColors.Add(series.ApexSeries.Stroke?.Color ?? "#d3d3d3"); //Default is light gray
                strokeDash.Add(series.ApexSeries.Stroke?.DashSpace ?? 0);
            }

            Options.Colors = strokeColors;
            Options.Stroke.Width = strokeWidths;
            Options.Stroke.Colors = strokeColors;
            Options.Stroke.DashArray = strokeDash;
        }

        private void SetDataLabels()
        {
            if (Options?.Series == null) { return; }

            if (Options.DataLabels == null) { Options.DataLabels = new DataLabels(); }
            if (Options.DataLabels.EnabledOnSeries == null) { Options.DataLabels.EnabledOnSeries = new List<double>(); }

            foreach (var series in Options.Series)
            {
                var index = Options.Series.FindIndex(e => e == series);
                if (series.ApexSeries.ShowDataLabels)
                {
                    if (!Options.DataLabels.EnabledOnSeries.Contains(index))
                    {
                        Options.DataLabels.EnabledOnSeries.Add(index);
                    }
                }
                else
                {
                    if (Options.DataLabels.EnabledOnSeries.Contains(index))
                    {
                        Options.DataLabels.EnabledOnSeries.Remove(index);
                    }
                }
            }

            if (Options.Series.Select(e=>e.ApexSeries).Any(e => e.ShowDataLabels))
            {
                Options.DataLabels.Enabled = true;
            }
            else
            {
                Options.DataLabels.Enabled = false;
            }
        }

        private void UpdateDataForNoAxisCharts()
        {
            if (!IsNoAxisChart)
            {
                Options.SeriesNonXAxis = null;
                Options.Labels = null;
                return;
            };

            if (Options.Series == null || !Options.Series.Any()) { return; }
            var noAxisSeries = Options.Series.First();
            var data = noAxisSeries.Data.Cast<DataPoint<TItem>>().ToList();
            Options.SeriesNonXAxis = data.Select(e => e.Y).Cast<object>().ToList();
            Options.Labels = data.Select(e => e.X?.ToString()).ToList();
        }

        public void FixLineDataSelection()
        {
            if ((Options.Chart.Type == ChartType.Line || Options.Chart.Type == ChartType.Area || Options.Chart.Type == ChartType.Radar) && OnDataPointSelection.HasDelegate)
            {
                if (Options.Tooltip == null) { Options.Tooltip = new Tooltip(); }
                if (Options.Markers == null) { Options.Markers = new Markers(); }

                if (Options.Markers.Size == null || Options.Markers.Size <= 0)
                {
                    Options.Markers.Size = 5;
                }

                Options.Tooltip.Intersect = true;
                Options.Tooltip.Shared = false;
            }
        }

        public void SetRerenderChart()
        {
            forceRender = true;
        }

        private async Task Render()
        {
            forceRender = false;
            SetSeries();
            SetStroke();
            SetDataLabels();
            FixLineDataSelection();
            UpdateDataForNoAxisCharts();

            var chartSerializer = new ChartSerializer();
            var serializerOptions = chartSerializer.GetOptions<TItem>();
            var jsonOptions = JsonSerializer.Serialize(Options, serializerOptions);
            await JSRuntime.InvokeVoidAsync("blazor_apexchart.renderChart", ObjectReference, ChartContainer, jsonOptions);
        }

        private void SetSeries()
        {
            Options.Series = new List<Series<TItem>>();
            var isMixed = apexSeries.Select(e => e.GetChartType()).Distinct().Count() > 1;

            foreach (var apxSeries in apexSeries)
            {
                var series = new Series<TItem>
                {
                    Data = apxSeries.GetData(),
                    Name = apxSeries.Name,
                    ApexSeries = apxSeries
                };
                Options.Series.Add(series);

                var seriesChartType = apxSeries.GetChartType();

                if (!isMixed)
                {
                    Options.Chart.Type = seriesChartType;
                }
                else
                {
                    series.Type = GetMixedChartType(seriesChartType);
                }
            }
        }


        private MixedType GetMixedChartType(ChartType chartType)
        {

            switch (chartType)
            {
                case ChartType.Line:
                    return MixedType.Line;
                case ChartType.Scatter:
                    return MixedType.Scatter;
                case ChartType.Area:
                    return MixedType.Area;
                case ChartType.Bubble:
                    return MixedType.Bubble;
                case ChartType.Bar:
                    if (Options?.PlotOptions?.Bar?.Horizontal == true)
                    {
                        return MixedType.Bar;
                    }
                    else
                    {
                        return MixedType.Column;
                    }

                default:
                    throw new Exception($"Chart Type {chartType} connot be mixed");
            }

        }


        public void Dispose()
        {
            GC.SuppressFinalize(this);
            if (Options.Chart?.ChartId != null && isReady)
            {
                InvokeAsync(async () => { await JSRuntime.InvokeVoidAsync("blazor_apexchart.destroyChart", Options.Chart.ChartId); });
            }

            if (ObjectReference != null)
            {
                ObjectReference.Dispose();
            }
        }

        [JSInvokable]
        public void DataPointSelected(DataPointSelection<TItem> selectedDataPoints)
        {
            if (OnDataPointSelection.HasDelegate)
            {
                var series = Options.Series.ElementAt(selectedDataPoints.SeriesIndex);
                var dataPoint = series.Data.ElementAt(selectedDataPoints.DataPointIndex);

                var selection = new SelectedData<TItem>
                {
                    Series = series,
                    DataPoint = dataPoint
                };

                OnDataPointSelection.InvokeAsync(selection);
            }
        }
    }
}