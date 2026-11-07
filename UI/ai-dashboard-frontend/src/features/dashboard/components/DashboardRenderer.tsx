
import {
  ResponsiveContainer,
  BarChart,
  Bar,
  LineChart,
  Line,
  AreaChart,
  Area,
  PieChart,
  Pie,
  Cell,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
} from "recharts";

import {
  TrendingUp,
  BarChart3,
  Table2,
  Activity,
  Database,
  Sparkles,
  CheckCircle2,
} from "lucide-react";

import type {
  DashboardConfiguration,
  DashboardComponent,
} from "../../chat/types/questionTypes";

import "./DashboardRenderer.css";

interface DashboardRendererProps {
  dashboard: DashboardConfiguration;
  data: Record<string, Array<Record<string, unknown>>>;
}

const DEFAULT_COLORS = [
  "#635bdb",
  "#16a085",
  "#f39c12",
  "#e76f51",
  "#3498db",
  "#9b59b6",
];

function getRows(
  data: DashboardRendererProps["data"],
  dataKey: string
) {
  if (!dataKey) return [];

  return data[dataKey] ?? [];
}

function getMetricValue(
  component: DashboardComponent,
  data: DashboardRendererProps["data"]
): unknown {
  const rows = getRows(data, component.dataKey);

  if (!rows.length) return null;

  const firstRow = rows[0];

  const preferredKeys = [
    component.dataKey,
    "value",
    "Value",
    "count",
    "Count",
    "total",
    "Total",
    "result",
    "Result",
  ];

  for (const key of preferredKeys) {
    if (
      key &&
      firstRow[key] !== undefined &&
      firstRow[key] !== null
    ) {
      return firstRow[key];
    }
  }

  const numericEntry = Object.entries(firstRow).find(
    ([, value]) =>
      typeof value === "number" ||
      (
        typeof value === "string" &&
        value.trim() !== "" &&
        !Number.isNaN(Number(value))
      )
  );

  if (numericEntry) {
    return numericEntry[1];
  }

  const firstEntry = Object.values(firstRow)[0];

  return firstEntry ?? null;
}

function formatValue(
  value: unknown,
  format: string
): string {
  if (value === null || value === undefined) {
    return "—";
  }

  const numericValue = Number(value);

  if (!Number.isFinite(numericValue)) {
    return String(value);
  }

  const normalizedFormat = format.toLowerCase();

  if (normalizedFormat.includes("percentage") ||
      normalizedFormat.includes("percent")) {
    const percentage =
      Math.abs(numericValue) <= 1
        ? numericValue * 100
        : numericValue;

    return `${percentage.toFixed(2)}%`;
  }

  if (normalizedFormat.includes("currency")) {
    return new Intl.NumberFormat("en-IN", {
      maximumFractionDigits: 2,
    }).format(numericValue);
  }

  return new Intl.NumberFormat("en-IN", {
    maximumFractionDigits: 2,
  }).format(numericValue);
}

function MetricCard({
  component,
  data,
}: {
  component: DashboardComponent;
  data: DashboardRendererProps["data"];
}) {
  const value = getMetricValue(component, data);

  return (
    <div
      className="dynamic-metric-card"
      style={{
        borderTopColor: component.color || "#635bdb",
      }}
    >
      <div className="dynamic-metric-header">
        <div>
          <p className="dynamic-metric-title">
            {component.title}
          </p>

          {component.subtitle && (
            <span className="dynamic-metric-subtitle">
              {component.subtitle}
            </span>
          )}
        </div>

        <div
          className="dynamic-metric-icon"
          style={{
            color: component.color || "#635bdb",
            backgroundColor: `${component.color || "#635bdb"}15`,
          }}
        >
          <TrendingUp size={21} />
        </div>
      </div>

      <h2 className="dynamic-metric-value">
        {formatValue(value, component.format)}
      </h2>

      {component.description && (
        <p className="dynamic-metric-description">
          {component.description}
        </p>
      )}
    </div>
  );
}

function ChartComponent({
  component,
  data,
}: {
  component: DashboardComponent;
  data: DashboardRendererProps["data"];
}) {
  const rows = getRows(data, component.dataKey);

  const chartType = component.chartType.toLowerCase();

  const series =
    component.series.length > 0
      ? component.series
      : component.yAxis
        ? [
            {
              name: component.yAxis,
              dataKey: component.yAxis,
              color: component.color || DEFAULT_COLORS[0],
            },
          ]
        : [];

  if (!rows.length) {
    return (
      <div className="dynamic-empty-data">
        <Database size={26} />
        <p>No chart data available.</p>
        <small>Data key: {component.dataKey}</small>
      </div>
    );
  }

  if (!series.length) {
    return (
      <div className="dynamic-empty-data">
        <p>No chart series configured.</p>
      </div>
    );
  }

  const renderChart = () => {
    if (chartType === "pie" || chartType === "doughnut" || chartType === "donut") {
      const pieSeries = series[0];

      return (
        <PieChart>
          <Tooltip />
          <Legend />

          <Pie
            data={rows}
            dataKey={pieSeries.dataKey}
            nameKey={component.xAxis}
            cx="50%"
            cy="50%"
            innerRadius={
              chartType === "pie" ? 0 : "55%"
            }
            outerRadius="75%"
            paddingAngle={3}
          >
            {rows.map((_, index) => (
              <Cell
                key={`pie-cell-${index}`}
                fill={DEFAULT_COLORS[index % DEFAULT_COLORS.length]}
              />
            ))}
          </Pie>
        </PieChart>
      );
    }

    if (chartType === "line") {
      return (
        <LineChart data={rows}>
          <CartesianGrid
            strokeDasharray="3 3"
            vertical={false}
          />

          <XAxis
            dataKey={component.xAxis}
            tick={{ fontSize: 11 }}
          />

          <YAxis tick={{ fontSize: 11 }} />

          <Tooltip />
          <Legend />

          {series.map((item, index) => (
            <Line
              key={item.dataKey}
              type="monotone"
              dataKey={item.dataKey}
              name={item.name}
              stroke={
                item.color ||
                DEFAULT_COLORS[index % DEFAULT_COLORS.length]
              }
              strokeWidth={3}
              dot={{ r: 3 }}
              activeDot={{ r: 5 }}
            />
          ))}
        </LineChart>
      );
    }

    if (chartType === "area") {
      return (
        <AreaChart data={rows}>
          <CartesianGrid
            strokeDasharray="3 3"
            vertical={false}
          />

          <XAxis
            dataKey={component.xAxis}
            tick={{ fontSize: 11 }}
          />

          <YAxis tick={{ fontSize: 11 }} />

          <Tooltip />
          <Legend />

          {series.map((item, index) => (
            <Area
              key={item.dataKey}
              type="monotone"
              dataKey={item.dataKey}
              name={item.name}
              stroke={
                item.color ||
                DEFAULT_COLORS[index % DEFAULT_COLORS.length]
              }
              fill={
                item.color ||
                DEFAULT_COLORS[index % DEFAULT_COLORS.length]
              }
              fillOpacity={0.15}
              strokeWidth={2}
            />
          ))}
        </AreaChart>
      );
    }

    return (
      <BarChart data={rows}>
        <CartesianGrid
          strokeDasharray="3 3"
          vertical={false}
        />

        <XAxis
          dataKey={component.xAxis}
          tick={{ fontSize: 11 }}
        />

        <YAxis tick={{ fontSize: 11 }} />

        <Tooltip />
        <Legend />

        {series.map((item, index) => (
          <Bar
            key={item.dataKey}
            dataKey={item.dataKey}
            name={item.name}
            fill={
              item.color ||
              DEFAULT_COLORS[index % DEFAULT_COLORS.length]
            }
            radius={[5, 5, 0, 0]}
          />
        ))}
      </BarChart>
    );
  };

  return (
    <div className="dynamic-chart-container">
      <ResponsiveContainer width="100%" height="100%">
        {renderChart()}
      </ResponsiveContainer>
    </div>
  );
}

function TableComponent({
  component,
  data,
}: {
  component: DashboardComponent;
  data: DashboardRendererProps["data"];
}) {
  const rows = getRows(data, component.dataKey);

  if (!rows.length) {
    return (
      <div className="dynamic-empty-data">
        <Database size={26} />
        <p>No table data available.</p>
      </div>
    );
  }

  const columns =
    component.tableColumns.length > 0
      ? component.tableColumns
      : Object.keys(rows[0]).map((key) => ({
          header: key,
          dataKey: key,
          format: "",
        }));

  return (
    <div className="dynamic-table-wrapper">
      <table className="dynamic-table">
        <thead>
          <tr>
            {columns.map((column) => (
              <th key={column.dataKey}>
                {column.header}
              </th>
            ))}
          </tr>
        </thead>

        <tbody>
          {rows.map((row, rowIndex) => (
            <tr key={rowIndex}>
              {columns.map((column) => (
                <td key={column.dataKey}>
                  {formatValue(
                    row[column.dataKey],
                    column.format
                  )}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function DashboardWidget({
  component,
  data,
}: {
  component: DashboardComponent;
  data: DashboardRendererProps["data"];
}) {
  const type = component.type.toLowerCase();

  const columnSpan = Math.max(1, component.columnSpan || 1);
  const rowSpan = Math.max(1, component.rowSpan || 1);

  return (
    <div
      className={`dynamic-dashboard-widget dynamic-widget-${type}`}
      style={{
        gridColumn: `span ${columnSpan}`,
        gridRow: `span ${rowSpan}`,
      }}
    >
      {type === "metric" ||
      type === "kpi" ||
      type === "stat" ? (
        <MetricCard component={component} data={data} />
      ) : type === "chart" ? (
        <div className="dynamic-panel">
          <div className="dynamic-panel-heading">
            <div className="dynamic-panel-icon">
              <BarChart3 size={19} />
            </div>

            <div>
              <h3>{component.title}</h3>
              {component.subtitle && (
                <p>{component.subtitle}</p>
              )}
            </div>
          </div>

          <ChartComponent
            component={component}
            data={data}
          />

          {component.description && (
            <p className="dynamic-panel-description">
              {component.description}
            </p>
          )}
        </div>
      ) : type === "table" ? (
        <div className="dynamic-panel">
          <div className="dynamic-panel-heading">
            <div className="dynamic-panel-icon">
              <Table2 size={19} />
            </div>

            <div>
              <h3>{component.title}</h3>
              {component.subtitle && (
                <p>{component.subtitle}</p>
              )}
            </div>
          </div>

          <TableComponent
            component={component}
            data={data}
          />
        </div>
      ) : (
        <div className="dynamic-panel">
          <div className="dynamic-empty-data">
            <Activity size={26} />
            <p>
              Unsupported dashboard component: {component.type}
            </p>
          </div>
        </div>
      )}
    </div>
  );
}

export default function DashboardRenderer({
  dashboard,
  data,
}: DashboardRendererProps) {
  const columns = Math.max(
    1,
    Math.min(dashboard.layoutConfig?.columns || 4, 6)
  );

  const gap = Math.max(
    0,
    dashboard.layoutConfig?.gap ?? 16
  );

  const components = [...dashboard.components].sort(
    (a, b) => a.order - b.order
  );

  return (
    <div className="generated-dashboard">
      <div className="generated-dashboard-header">
        <div>
          <div className="generated-dashboard-label">
            <Sparkles size={15} />
            AI GENERATED DASHBOARD
          </div>

          <h1>{dashboard.title}</h1>

          {dashboard.description && (
            <p>{dashboard.description}</p>
          )}
        </div>

        <div className="generated-dashboard-status">
          <CheckCircle2 size={15} />
          Generated
        </div>
      </div>

      {components.length === 0 ? (
        <div className="dynamic-empty-data">
          <Database size={30} />
          <p>No dashboard components were generated.</p>
        </div>
      ) : (
        <div
          className="generated-dashboard-grid"
          style={{
            gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))`,
            gap: `${gap}px`,
          }}
        >
          {components.map((component, index) => (
            <DashboardWidget
              key={component.id || index}
              component={component}
              data={data}
            />
          ))}
        </div>
      )}
    </div>
  );
}
