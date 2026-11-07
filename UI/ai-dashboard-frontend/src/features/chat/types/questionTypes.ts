
export interface QuestionRequest {
  userId: string;
  chatId: string;
  query: string;
}

export interface DashboardChartSeries {
  name: string;
  dataKey: string;
  color: string;
}

export interface DashboardTableColumn {
  header: string;
  dataKey: string;
  format: string;
}

export interface DashboardComponent {
  id: string;
  type: string;
  title: string;
  subtitle: string;
  order: number;
  columnSpan: number;
  rowSpan: number;
  dataKey: string;
  chartType: string;
  xAxis: string;
  yAxis: string;
  description: string;
  format: string;
  color: string;
  series: DashboardChartSeries[];
  tableColumns: DashboardTableColumn[];
}

export interface DashboardConfiguration {
  title: string;
  description: string;
  layout: string;
  theme: string;
  layoutConfig: {
    columns: number;
    gap: number;
  };
  components: DashboardComponent[];
}

export interface QuestionResult {
  queryPlan: Record<string, unknown>;
  data: Record<string, Array<Record<string, unknown>>>;
  dashboard: DashboardConfiguration | null;
}
