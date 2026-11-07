
import apiClient from "../../../services/apiClient";
import type {
  QuestionRequest,
  QuestionResult,
} from "../types/questionTypes";

interface RawQuestionResponse {
  queryPlan?: Record<string, unknown>;
  data?: Record<string, Array<Record<string, unknown>>>;
  dashboard?: QuestionResult["dashboard"];

  prompt?: {
    queryPlan?: Record<string, unknown>;
    data?: Record<string, Array<Record<string, unknown>>>;
    dashboard?: QuestionResult["dashboard"];
  };
}

export const askQuestion = async (
  request: QuestionRequest
): Promise<QuestionResult> => {
  if (!request.userId?.trim()) {
    throw new Error("User ID is missing.");
  }

  if (!request.chatId?.trim()) {
    throw new Error("Chat ID is missing.");
  }

  if (!request.query?.trim()) {
    throw new Error("Question cannot be empty.");
  }

  const payload: QuestionRequest = {
    userId: request.userId.trim(),
    chatId: request.chatId.trim(),
    query: request.query.trim(),
  };

  const response = await apiClient.post<RawQuestionResponse>(
    "/Question",
    payload
  );

  console.log("Raw Question API Response:", response.data);

  // Support both response.data.dashboard and response.data.prompt.dashboard
  const result = response.data.prompt ?? response.data;

  const originalData = result.data ?? {};

  // Preserve the original sheet data.
  // Also expose individual aggregation keys for dashboard components.
  const normalizedData: Record<
    string,
    Array<Record<string, unknown>>
  > = { ...originalData };

  Object.values(originalData).forEach((rows) => {
    rows.forEach((row) => {
      Object.entries(row).forEach(([key, value]) => {
        normalizedData[key] = [
          {
            [key]: value,
          },
        ];
      });
    });
  });

  const normalizedResult: QuestionResult = {
    queryPlan: result.queryPlan ?? {},
    data: normalizedData,
    dashboard: result.dashboard ?? null,
  };

  console.log("Normalized Question Result:", normalizedResult);
  console.log("Dashboard Configuration:", normalizedResult.dashboard);
  console.log("Dashboard Data:", normalizedResult.data);

  return normalizedResult;
};
