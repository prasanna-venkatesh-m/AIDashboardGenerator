using System;
using System.Collections.Generic;
using System.Text;

namespace AiDashboardGenerator.Models
{
    public class DashboardConfiguration
    {
        public string Title { get; set; } = "";

        public string Description { get; set; } = "";

        public string Layout { get; set; } = "grid";

        public string Theme { get; set; } = "light";

        public DashboardLayout LayoutConfig { get; set; } = new();

        public List<DashboardComponent> Components { get; set; } = [];
    }

    public class DashboardLayout
    {
        public int Columns { get; set; } = 4;

        public int Gap { get; set; } = 16;
    }

    public class DashboardComponent
    {
        public string Id { get; set; } = "";

        public string Type { get; set; } = "";

        public string Title { get; set; } = "";

        public string Subtitle { get; set; } = "";

        public int Order { get; set; }

        public int ColumnSpan { get; set; } = 1;

        public int RowSpan { get; set; } = 1;

        public string DataKey { get; set; } = "";

        public string ChartType { get; set; } = "";

        public string XAxis { get; set; } = "";

        public string YAxis { get; set; } = "";

        public string Description { get; set; } = "";

        public string Format { get; set; } = "";

        public string Color { get; set; } = "";

        public List<DashboardChartSeries> Series { get; set; } = [];

        public List<DashboardTableColumn> TableColumns { get; set; } = [];
    }

    public class DashboardChartSeries
    {
        public string Name { get; set; } = "";

        public string DataKey { get; set; } = "";

        public string Color { get; set; } = "";
    }

    public class DashboardTableColumn
    {
        public string Header { get; set; } = "";

        public string DataKey { get; set; } = "";

        public string Format { get; set; } = "";
    }
}
