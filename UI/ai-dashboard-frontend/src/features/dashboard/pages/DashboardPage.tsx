
import { useRef, useState } from "react";
import {
  Plus,
  MessageSquare,
  UploadCloud,
  Send,
  Loader2,
  Sparkles,
  PanelLeftClose,
  AlertCircle,
  CheckCircle2,
  BarChart3,
  Table2,
} from "lucide-react";

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

import { uploadExcelFile } from "../../files/services/fileService";
import type { FileUploadResponse } from "../../files/types/fileTypes";
import { askQuestion } from "../../chat/services/questionService";
import type {
  QuestionResult,
  DashboardConfiguration,
  DashboardComponent,
} from "../../chat/types/questionTypes";

import "./DashboardPage.css";

interface ChatMessage {
  id: number;
  role: "user" | "assistant";
  content: string;
}

const COLORS = [
  "#635BDB",
  "#18A77A",
  "#F2A900",
  "#E87979",
  "#4094D8",
  "#9B6AD8",
];

function getRows(
  data: QuestionResult["data"],
  dataKey: string
): Record<string, unknown>[] {
  if (!dataKey?.trim()) return [];

  if (data[dataKey]) return data[dataKey] ?? [];

  const matchingKey = Object.keys(data).find(
    (key) => key.toLowerCase() === dataKey.toLowerCase()
  );

  return matchingKey ? data[matchingKey] ?? [] : [];
}

function getAllRows(
  data: QuestionResult["data"]
): Record<string, unknown>[] {
  return Object.values(data).flatMap((rows) => rows ?? []);
}

function findValueInRows(
  rows: Record<string, unknown>[],
  dataKey: string
): unknown {
  const targetKey = dataKey?.trim().toLowerCase();
  if (!targetKey) return null;

  for (const row of rows) {
    const matchingKey = Object.keys(row).find(
      (key) => key.toLowerCase() === targetKey
    );

    if (
      matchingKey &&
      row[matchingKey] !== null &&
      row[matchingKey] !== undefined
    ) {
      return row[matchingKey];
    }
  }

  return null;
}

function getMetricValue(
  component: DashboardComponent,
  data: QuestionResult["data"]
): unknown {
  const rows = getAllRows(data);
  const directValue = findValueInRows(rows, component.dataKey);

  if (directValue !== null) return directValue;

  const keyedRows = getRows(data, component.dataKey);
  if (!keyedRows.length) return null;

  const row = keyedRows[0];
  const preferredKeys = ["value", "count", "total", "result"];

  for (const preferredKey of preferredKeys) {
    const matchingKey = Object.keys(row).find(
      (key) => key.toLowerCase() === preferredKey
    );

    if (matchingKey && row[matchingKey] !== null && row[matchingKey] !== undefined) {
      return row[matchingKey];
    }
  }

  return Object.values(row).find(
    (value) => value !== null && value !== undefined
  ) ?? null;
}

function formatMetric(value: unknown, format: string): string {
  if (value === null || value === undefined) return "—";

  const number = Number(value);

  if (!Number.isFinite(number)) return String(value);

  if (
    format.toLowerCase().includes("percentage") ||
    format.toLowerCase().includes("percent")
  ) {
    const percentage = Math.abs(number) <= 1 ? number * 100 : number;
    return `${percentage.toFixed(2)}%`;
  }

  return new Intl.NumberFormat("en-IN", {
    maximumFractionDigits: 2,
  }).format(number);
}

function MetricWidget({
  component,
  data,
}: {
  component: DashboardComponent;
  data: QuestionResult["data"];
}) {
  const value = getMetricValue(component, data);

  return (
    <div
      className="chat-metric-card"
      style={{ borderTopColor: component.color || COLORS[0] }}
    >
      <div className="chat-metric-top">
        <span>{component.title}</span>
        <div
          className="chat-metric-icon"
          style={{ color: component.color || COLORS[0] }}
        >
          <BarChart3 size={19} />
        </div>
      </div>

      <strong>{formatMetric(value, component.format)}</strong>

      {component.subtitle && <small>{component.subtitle}</small>}
    </div>
  );
}

function titleFromKey(key: string): string {
  return key
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/[_-]/g, " ")
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function normalizeChartType(value: string): string {
  const type = (value || "bar").toLowerCase().replace(/[\s-]/g, "_");

  if (type.includes("donut") || type.includes("doughnut")) return "donut";
  if (type.includes("pie")) return "pie";
  if (type.includes("horizontal") && type.includes("bar")) return "horizontal_bar";
  if (type.includes("bar")) return "bar";
  if (type.includes("line")) return "line";
  if (type.includes("area")) return "area";

  return type;
}

function inferSeriesFromTitle(
  component: DashboardComponent,
  data: QuestionResult["data"]
): Array<{ name: string; dataKey: string; color?: string }> {
  const ignoredWords = new Set([
    "vs", "and", "or", "the", "of", "for", "by", "number", "total",
    "distribution", "status", "comparison", "chart",
  ]);

  const titleWords = (component.title || "")
    .toLowerCase()
    .split(/[^a-z0-9]+/)
    .filter((word) => word && !ignoredWords.has(word));

  if (titleWords.length === 0) return [];

  const rows = getAllRows(data);
  const firstRow = rows.find((row) => Object.keys(row).length > 0);
  if (!firstRow) return [];

  const matches = Object.keys(firstRow)
    .map((key) => {
      const keyWords = key
        .toLowerCase()
        .split(/[^a-z0-9]+/)
        .filter(Boolean);

      const score = keyWords.filter((word) => titleWords.includes(word)).length;
      const value = firstRow[key];

      const isNumeric =
        typeof value === "number" ||
        (typeof value === "string" &&
          value.trim() !== "" &&
          Number.isFinite(Number(value)));

      return { key, score, isNumeric };
    })
    .filter((item) => item.isNumeric && item.score >= 2)
    .sort((a, b) => b.score - a.score)
    .slice(0, 6);

  return matches.map((item, index) => ({
    name: titleFromKey(item.key),
    dataKey: item.key,
    color: COLORS[index % COLORS.length],
  }));
}

function ChartWidget({
  component,
  data,
}: {
  component: DashboardComponent;
  data: QuestionResult["data"];
}) {
  const chartType = normalizeChartType(component.chartType || component.type);

  const suppliedSeries = Array.isArray(component.series)
    ? component.series
    : [];

  const configuredSeries = suppliedSeries
    .filter((item) => item?.dataKey)
    .map((item) => ({
      name: item.name || titleFromKey(item.dataKey),
      dataKey: item.dataKey,
      color: item.color,
    }));

  const resolvedSeries =
    configuredSeries.length > 0
      ? configuredSeries
      : inferSeriesFromTitle(component, data);

  const allRows = getAllRows(data);
  const sourceRows =
    getRows(data, component.dataKey).length > 0
      ? getRows(data, component.dataKey)
      : allRows;

  const hasConfiguredCategoryAxis =
    !!component.xAxis &&
    sourceRows.some((row) =>
      Object.keys(row).some(
        (key) => key.toLowerCase() === component.xAxis.toLowerCase()
      )
    );

  let chartRows = sourceRows;
  let chartSeries = resolvedSeries;
  let categoryKey = component.xAxis || "category";
  let valueKey = configuredSeries[0]?.dataKey || component.yAxis || "value";
  let isMetricComparison = false;

  // Aggregate query results often contain one row with multiple metrics:
  // { paid_clients: 8373, unpaid_clients: 67916 }.
  // Convert those metrics into category/value rows for pie and bar charts.
  const aggregateRow = allRows.find((row) =>
    resolvedSeries.length > 0 &&
    resolvedSeries.every((series) =>
      Object.keys(row).some(
        (key) => key.toLowerCase() === series.dataKey.toLowerCase()
      )
    )
  );

  // A one-row grouped dataset is still valid chart data. Only convert
  // aggregate metrics into category/value rows when the requested category
  // axis is absent from the source rows.
  const isSingleAggregateRow =
    !!aggregateRow &&
    !hasConfiguredCategoryAxis &&
    sourceRows.length <= 1;

  if (
    isSingleAggregateRow &&
    resolvedSeries.length > 0 &&
    ["pie", "donut", "bar", "horizontal_bar"].includes(chartType)
  ) {
    const metricRows = resolvedSeries
      .map((series) => {
        const value = findValueInRows([aggregateRow], series.dataKey);

        return {
          category: series.name,
          value: Number(value),
        };
      })
      .filter((row) => Number.isFinite(row.value));

    chartRows = metricRows;
    categoryKey = "category";
    valueKey = "value";
    chartSeries = [
      {
        name: component.title || "Value",
        dataKey: "value",
        color: component.color || COLORS[0],
      },
    ];
    isMetricComparison = true;
  } else if (chartSeries.length === 0 && component.yAxis) {
    chartSeries = [
      {
        name: component.yAxis,
        dataKey: component.yAxis,
        color: component.color || COLORS[0],
      },
    ];
  }

  if (!chartRows.length || !chartSeries.length) {
    return (
      <div className="chat-no-data">
        <BarChart3 size={25} />
        <span>No chart data available</span>
      </div>
    );
  }

  if (chartType === "pie" || chartType === "donut") {
    // For ordinary grouped data, use the configured series data key.
    // For one-row aggregate data, use the generated category/value rows.
    const pieDataKey = isMetricComparison
      ? valueKey
      : chartSeries[0].dataKey;

    const pieNameKey = isMetricComparison
      ? categoryKey
      : component.xAxis || "name";

    return (
      <div className="chat-chart">
        <ResponsiveContainer width="100%" height="100%">
          <PieChart>
            <Tooltip />
            <Legend />
            <Pie
              data={chartRows}
              dataKey={pieDataKey}
              nameKey={pieNameKey}
              cx="50%"
              cy="50%"
              outerRadius="75%"
              innerRadius={chartType === "pie" ? 0 : "52%"}
              paddingAngle={3}
            >
              {chartRows.map((_, index) => (
                <Cell
                  key={index}
                  fill={COLORS[index % COLORS.length]}
                />
              ))}
            </Pie>
          </PieChart>
        </ResponsiveContainer>
      </div>
    );
  }

  if (chartType === "line") {
    return (
      <div className="chat-chart">
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={chartRows}>
            <CartesianGrid strokeDasharray="3 3" vertical={false} />
            <XAxis dataKey={categoryKey} />
            <YAxis />
            <Tooltip />
            <Legend />
            {chartSeries.map((item, index) => (
              <Line
                key={item.dataKey}
                type="monotone"
                dataKey={item.dataKey}
                name={item.name}
                stroke={item.color || COLORS[index % COLORS.length]}
                strokeWidth={3}
              />
            ))}
          </LineChart>
        </ResponsiveContainer>
      </div>
    );
  }

  if (chartType === "area") {
    return (
      <div className="chat-chart">
        <ResponsiveContainer width="100%" height="100%">
          <AreaChart data={chartRows}>
            <CartesianGrid strokeDasharray="3 3" vertical={false} />
            <XAxis dataKey={categoryKey} />
            <YAxis />
            <Tooltip />
            <Legend />
            {chartSeries.map((item, index) => (
              <Area
                key={item.dataKey}
                type="monotone"
                dataKey={item.dataKey}
                name={item.name}
                stroke={item.color || COLORS[index % COLORS.length]}
                fill={item.color || COLORS[index % COLORS.length]}
                fillOpacity={0.15}
              />
            ))}
          </AreaChart>
        </ResponsiveContainer>
      </div>
    );
  }

  const isHorizontal = chartType === "horizontal_bar";

  return (
    <div className="chat-chart">
      <ResponsiveContainer width="100%" height="100%">
        <BarChart
          data={chartRows}
          layout={isHorizontal ? "vertical" : "horizontal"}
          margin={{ top: 8, right: 16, bottom: 8, left: isHorizontal ? 24 : 0 }}
        >
          <CartesianGrid strokeDasharray="3 3" vertical={!isHorizontal} />
          {isHorizontal ? (
            <>
              <XAxis type="number" />
              <YAxis
                type="category"
                dataKey={categoryKey}
                width={110}
              />
            </>
          ) : (
            <>
              <XAxis dataKey={categoryKey} />
              <YAxis />
            </>
          )}
          <Tooltip />
          <Legend />
          {chartSeries.map((item, index) => (
            <Bar
              key={item.dataKey}
              dataKey={item.dataKey}
              name={item.name}
              fill={item.color || COLORS[index % COLORS.length]}
              radius={isHorizontal ? [0, 5, 5, 0] : [5, 5, 0, 0]}
            />
          ))}
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}

function TableWidget({
  component,
  data,
}: {
  component: DashboardComponent;
  data: QuestionResult["data"];
}) {
  const columns =
    component.tableColumns?.length > 0
      ? component.tableColumns
      : [];

  let rows = getRows(data, component.dataKey);

  // A summary table may have an empty dataKey and columns named
  // metric_name / metric_value. Build those rows from the actual API result.
  if (!rows.length && !component.dataKey) {
    const sourceRow = Object.values(data)
      .flatMap((sheetRows) => sheetRows ?? [])
      .find((row) => Object.keys(row).length > 1);

    if (sourceRow) {
      const expectsMetricName = columns.some(
        (column) => column.dataKey.toLowerCase() === "metric_name"
      );
      const expectsMetricValue = columns.some(
        (column) => column.dataKey.toLowerCase() === "metric_value"
      );

      if (expectsMetricName && expectsMetricValue) {
        rows = Object.entries(sourceRow).map(([key, value]) => ({
          metric_name: key.replace(/_/g, " ").replace(/\b\w/g, (letter) => letter.toUpperCase()),
          metric_value: value,
        }));
      } else {
        rows = [sourceRow];
      }
    }
  }

  if (!rows.length) {
    return (
      <div className="chat-no-data">
        No table data available
      </div>
    );
  }

  const resolvedColumns =
    columns.length > 0
      ? columns
      : Object.keys(rows[0]).map((key) => ({
          header: key,
          dataKey: key,
          format: "",
        }));

  return (
    <div className="chat-table-scroll">
      <table className="chat-data-table">
        <thead>
          <tr>
            {resolvedColumns.map((column) => (
              <th key={column.dataKey}>{column.header}</th>
            ))}
          </tr>
        </thead>

        <tbody>
          {rows.map((row, index) => (
            <tr key={index}>
              {resolvedColumns.map((column) => (
                <td key={column.dataKey}>
                  {formatMetric(row[column.dataKey], column.format)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function DashboardView({
  dashboard,
  data,
}: {
  dashboard: DashboardConfiguration;
  data: QuestionResult["data"];
}) {
  const components = [...(dashboard.components || [])].sort(
    (a, b) => a.order - b.order
  );

  const columns = Math.max(
    1,
    Math.min(dashboard.layoutConfig?.columns || 4, 6)
  );

  return (
    <div className="chat-dashboard">
      <div className="chat-dashboard-header">
        <div>
          <div className="chat-dashboard-label">
            <Sparkles size={14} />
            AI GENERATED DASHBOARD
          </div>

          <h2>{dashboard.title || "Business Dashboard"}</h2>

          {dashboard.description && (
            <p>{dashboard.description}</p>
          )}
        </div>

        <span className="dashboard-generated-badge">
          <CheckCircle2 size={14} />
          Generated
        </span>
      </div>

      {components.length > 0 ? (
        <div
          className="chat-dashboard-grid"
          style={{
            gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))`,
            gap: `${dashboard.layoutConfig?.gap ?? 16}px`,
          }}
        >
          {components.map((component, index) => {
            const type = (component.type || "").toLowerCase();

            const span = Math.max(
              1,
              Math.min(component.columnSpan || 1, columns)
            );

            return (
              <div
                key={component.id || index}
                className="chat-dashboard-widget"
                style={{
                  gridColumn: `span ${span}`,
                }}
              >
                {["metric", "kpi", "stat", "metric_card", "kpi_card", "stat_card"].includes(type) ? (
                  <MetricWidget
                    component={component}
                    data={data}
                  />
                ) : ["table", "summary_table", "data_table"].includes(type) ? (
                  <div className="chat-panel">
                    <div className="chat-panel-heading">
                      <Table2 size={18} />
                      <h3>{component.title}</h3>
                    </div>

                    <TableWidget
                      component={component}
                      data={data}
                    />
                  </div>
                ) : (
                  <div className="chat-panel">
                    <div className="chat-panel-heading">
                      <BarChart3 size={18} />
                      <h3>{component.title}</h3>
                    </div>

                    <ChartWidget
                      component={component}
                      data={data}
                    />
                  </div>
                )}
              </div>
            );
          })}
        </div>
      ) : (
        <div className="chat-no-data">
          Dashboard configuration has no components.
        </div>
      )}
    </div>
  );
}

export default function DashboardPage() {
  const fileInputRef = useRef<HTMLInputElement>(null);
  const chatEndRef = useRef<HTMLDivElement>(null);

  const [uploadedFile, setUploadedFile] =
    useState<FileUploadResponse | null>(null);

  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [query, setQuery] = useState("");
  const [questionResult, setQuestionResult] =
    useState<QuestionResult | null>(null);

  const [isUploading, setIsUploading] = useState(false);
  const [isAsking, setIsAsking] = useState(false);
  const [sidebarOpen, setSidebarOpen] = useState(true);
  const [error, setError] = useState("");

  const startNewChat = () => {
    setUploadedFile(null);
    setMessages([]);
    setQuestionResult(null);
    setQuery("");
    setError("");

    if (fileInputRef.current) {
      fileInputRef.current.value = "";
    }

    setTimeout(() => fileInputRef.current?.click(), 50);
  };

  const handleFileSelection = async (file?: File) => {
    if (!file) return;

    const extension = file.name.split(".").pop()?.toLowerCase();

    if (!["xlsx", "xls"].includes(extension || "")) {
      setError("Please select an Excel file (.xlsx or .xls).");
      return;
    }

    if (file.size > 20 * 1024 * 1024) {
      setError("File size must not exceed 20 MB.");
      return;
    }

    try {
      setIsUploading(true);
      setError("");

      const response = await uploadExcelFile(file);

      if (!response.userId || !response.chatId) {
        throw new Error(
          "The server did not return a valid UserId and ChatId."
        );
      }

      setUploadedFile(response);
      setMessages([
        {
          id: Date.now(),
          role: "assistant",
          content: `${response.fileName} uploaded successfully. Ask me anything about your Excel data.`,
        },
      ]);

      setQuestionResult(null);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Unable to upload the Excel file."
      );
    } finally {
      setIsUploading(false);
    }
  };

  const handleSendMessage = async () => {
    if (!uploadedFile || !query.trim() || isAsking) return;

    const currentQuery = query.trim();

    setMessages((previous) => [
      ...previous,
      {
        id: Date.now(),
        role: "user",
        content: currentQuery,
      },
    ]);

    setQuery("");
    setError("");
    setIsAsking(true);

    try {
      const response = await askQuestion({
        userId: uploadedFile.userId,
        chatId: uploadedFile.chatId,
        query: currentQuery,
      });

      setQuestionResult(response);

      setMessages((previous) => [
        ...previous,
        {
          id: Date.now() + 1,
          role: "assistant",
          content: response.dashboard
            ? "Your dashboard has been generated successfully."
            : "Your data has been processed. No dashboard configuration was returned.",
        },
      ]);

      setTimeout(() => {
        chatEndRef.current?.scrollIntoView({
          behavior: "smooth",
        });
      }, 100);
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Unable to process your question."
      );

      setMessages((previous) => [
        ...previous,
        {
          id: Date.now() + 1,
          role: "assistant",
          content: "I couldn't process that question. Please try again.",
        },
      ]);
    } finally {
      setIsAsking(false);
    }
  };

  const handleKeyDown = (
    event: React.KeyboardEvent<HTMLTextAreaElement>
  ) => {
    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      handleSendMessage();
    }
  };

  return (
    <div className="chat-app">

      {/* Sidebar */}
      <aside
        className={`chat-sidebar ${
          sidebarOpen ? "" : "chat-sidebar--closed"
        }`}
      >
        <div className="chat-sidebar-top">
          <div className="chat-brand">
            <div className="chat-brand-icon">
              <Sparkles size={20} />
            </div>
            <span>InsightAI</span>
          </div>

          <button
            className="sidebar-toggle"
            onClick={() => setSidebarOpen(false)}
            title="Close sidebar"
          >
            <PanelLeftClose size={19} />
          </button>
        </div>

        <button
          className="new-chat-button"
          onClick={startNewChat}
          disabled={isUploading || isAsking}
        >
          <Plus size={19} />
          <span>New Chat</span>
        </button>

        <div className="sidebar-history">
          <span className="sidebar-section-label">
            CURRENT CHAT
          </span>

          {uploadedFile && (
            <div className="sidebar-chat-item">
              <MessageSquare size={16} />
              <span>{uploadedFile.fileName}</span>
            </div>
          )}
        </div>

        <div className="sidebar-footer">
          <div className="sidebar-user-avatar">U</div>
          <div>
            <strong>Demo User</strong>
            <span>Excel AI Workspace</span>
          </div>
        </div>
      </aside>

      {/* Main Chat */}
      <main className="chat-main">

        {!sidebarOpen && (
          <button
            className="open-sidebar-button"
            onClick={() => setSidebarOpen(true)}
          >
            <PanelLeftClose size={19} />
          </button>
        )}

        <input
          ref={fileInputRef}
          type="file"
          accept=".xlsx,.xls"
          hidden
          onChange={(event) =>
            handleFileSelection(event.target.files?.[0])
          }
        />

        {/* Upload Loading */}
        {isUploading ? (
          <div className="chat-empty-state">
            <div className="empty-state-icon">
              <Loader2 className="loading-spinner" size={30} />
            </div>
            <h2>Uploading your Excel file</h2>
            <p>Please wait while we process your file.</p>
          </div>
        ) : !uploadedFile ? (

          /* Initial State */
          <div className="chat-empty-state">
            <div className="empty-state-icon">
              <Sparkles size={30} />
            </div>

            <h1>Welcome to InsightAI</h1>

            <p>
              Upload an Excel file and start asking questions
              about your business data.
            </p>

            <button
              className="start-upload-button"
              onClick={startNewChat}
            >
              <UploadCloud size={19} />
              Upload Excel File
            </button>

            <span className="supported-format">
              Supports XLSX and XLS files · Maximum 20 MB
            </span>
          </div>

        ) : (

          /* Chat Mode */
          <>
            <header className="chat-header">
              <div>
                <span className="chat-header-label">
                  EXCEL WORKSPACE
                </span>
                <h2>{uploadedFile.fileName}</h2>
              </div>

              <div className="chat-file-status">
                <CheckCircle2 size={15} />
                File connected
              </div>
            </header>

            <div className="chat-content">

              {/* Messages */}
              <div className="chat-messages">
                {messages.map((message) => (
                  <div
                    key={message.id}
                    className={`chat-message chat-message--${message.role}`}
                  >
                    {message.role === "assistant" && (
                      <div className="message-avatar">
                        <Sparkles size={17} />
                      </div>
                    )}

                    <div className="message-content">
                      {message.content}
                    </div>
                  </div>
                ))}

                {isAsking && (
                  <div className="chat-message chat-message--assistant">
                    <div className="message-avatar">
                      <Sparkles size={17} />
                    </div>
                    <div className="message-content typing-message">
                      <Loader2 size={16} className="loading-spinner" />
                      Analyzing your Excel data...
                    </div>
                  </div>
                )}
              </div>

              {/* Generated Dashboard */}
              {questionResult?.dashboard && (
                <DashboardView
                  dashboard={questionResult.dashboard}
                  data={questionResult.data}
                />
              )}

              {questionResult && !questionResult.dashboard && (
                <div className="chat-no-data">
                  The API returned data but no dashboard configuration.
                </div>
              )}

              <div ref={chatEndRef} />
            </div>

            {/* Message Composer */}
            <div className="chat-composer-area">
              {error && (
                <div className="chat-error">
                  <AlertCircle size={16} />
                  {error}
                </div>
              )}

              <div className="chat-composer">
                <textarea
                  value={query}
                  onChange={(event) => setQuery(event.target.value)}
                  onKeyDown={handleKeyDown}
                  placeholder="Ask anything about your Excel data..."
                  rows={1}
                  disabled={isAsking}
                />

                <button
                  className="send-message-button"
                  onClick={handleSendMessage}
                  disabled={!query.trim() || isAsking}
                  title="Send message"
                >
                  {isAsking ? (
                    <Loader2 size={19} className="loading-spinner" />
                  ) : (
                    <Send size={19} />
                  )}
                </button>
              </div>

              <p className="composer-disclaimer">
                InsightAI can make mistakes. Verify important business figures.
              </p>
            </div>
          </>
        )}
      </main>
    </div>
  );
}
