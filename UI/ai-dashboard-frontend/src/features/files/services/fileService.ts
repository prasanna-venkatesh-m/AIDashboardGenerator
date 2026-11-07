import apiClient from "../../../services/apiClient";
import { appConfig } from "../../../config/appConfig";
import type { FileUploadResponse } from "../types/fileTypes";

export const uploadExcelFile = async (
  file: File,
  onProgress?: (progress: number) => void
): Promise<FileUploadResponse> => {
  const formData = new FormData();

  formData.append("UserId", appConfig.userId);
  formData.append("File", file);

  const response = await apiClient.post<FileUploadResponse>(
    "/Files/upload",
    formData,
    {
      headers: {
        "Content-Type": "multipart/form-data",
      },
      onUploadProgress: (event) => {
        if (event.total && onProgress) {
          const progress = Math.round(
            (event.loaded * 100) / event.total
          );

          onProgress(progress);
        }
      },
    }
  );

  return response.data;
};