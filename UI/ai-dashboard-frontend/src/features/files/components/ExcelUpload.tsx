
import { useRef, useState } from "react";
import {
  FileSpreadsheet,
  UploadCloud,
  X,
  CheckCircle2,
  AlertCircle,
  Loader2,
} from "lucide-react";

import { uploadExcelFile } from "../services/fileService";
import type { FileUploadResponse } from "../types/fileTypes";
import "./ExcelUpload.css";

interface ExcelUploadProps {
  onUploadSuccess?: (file: FileUploadResponse) => void;
}

const MAX_FILE_SIZE = 20 * 1024 * 1024;

export default function ExcelUpload({
  onUploadSuccess,
}: ExcelUploadProps) {
  const fileInputRef = useRef<HTMLInputElement>(null);

  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [isDragging, setIsDragging] = useState(false);
  const [isUploading, setIsUploading] = useState(false);
  const [progress, setProgress] = useState(0);
  const [uploadedFile, setUploadedFile] =
    useState<FileUploadResponse | null>(null);
  const [error, setError] = useState("");

  const validateFile = (file: File): boolean => {
    const extension = file.name.split(".").pop()?.toLowerCase();

    if (!["xlsx", "xls"].includes(extension ?? "")) {
      setError("Please select a valid Excel file (.xlsx or .xls).");
      return false;
    }

    if (file.size > MAX_FILE_SIZE) {
      setError("File size must not exceed 20 MB.");
      return false;
    }

    if (file.size === 0) {
      setError("The selected file is empty.");
      return false;
    }

    setError("");
    setUploadedFile(null);
    setProgress(0);
    return true;
  };

  const handleFileSelection = (file?: File) => {
    if (!file) return;

    if (validateFile(file)) {
      setSelectedFile(file);
    } else {
      setSelectedFile(null);
    }
  };

  const handleDrop = (event: React.DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    setIsDragging(false);

    if (isUploading) return;

    handleFileSelection(event.dataTransfer.files[0]);
  };

  const handleUpload = async () => {
    if (!selectedFile || isUploading) return;

    try {
      setIsUploading(true);
      setError("");
      setProgress(0);

      const response = await uploadExcelFile(
        selectedFile,
        setProgress
      );

      setUploadedFile(response);
      setSelectedFile(null);
      setProgress(100);

      if (fileInputRef.current) {
        fileInputRef.current.value = "";
      }

      onUploadSuccess?.(response);
    } catch (err: unknown) {
      console.error("Excel upload failed:", err);

      if (typeof err === "object" && err !== null && "response" in err) {
        const response = (
          err as { response?: { data?: unknown } }
        ).response;

        setError(
          typeof response?.data === "string"
            ? response.data
            : "Unable to upload the file. Please try again."
        );
      } else {
        setError("Unable to connect to the server. Please try again.");
      }
    } finally {
      setIsUploading(false);
    }
  };

  const handleRemoveFile = () => {
    if (isUploading) return;

    setSelectedFile(null);
    setError("");
    setProgress(0);

    if (fileInputRef.current) {
      fileInputRef.current.value = "";
    }
  };

  const formatFileSize = (bytes: number) => {
    if (bytes < 1024 * 1024) {
      return `${(bytes / 1024).toFixed(1)} KB`;
    }

    return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
  };

  return (
    <div className="excel-upload">
      <div className="excel-upload__header">
        <div className="excel-upload__icon">
          <FileSpreadsheet size={24} />
        </div>

        <div>
          <h2>Upload Excel File</h2>
          <p>
            Upload your Excel data to generate an AI dashboard.
          </p>
        </div>
      </div>

      <div
        className={`excel-upload__dropzone ${
          isDragging ? "excel-upload__dropzone--active" : ""
        }`}
        onDragOver={(event) => {
          event.preventDefault();
          if (!isUploading) setIsDragging(true);
        }}
        onDragLeave={() => setIsDragging(false)}
        onDrop={handleDrop}
        onClick={() => {
          if (!isUploading) fileInputRef.current?.click();
        }}
        role="button"
        tabIndex={0}
        onKeyDown={(event) => {
          if (
            !isUploading &&
            (event.key === "Enter" || event.key === " ")
          ) {
            event.preventDefault();
            fileInputRef.current?.click();
          }
        }}
      >
        <input
          ref={fileInputRef}
          type="file"
          accept=".xlsx,.xls"
          hidden
          disabled={isUploading}
          onChange={(event) =>
            handleFileSelection(event.target.files?.[0])
          }
        />

        <div className="excel-upload__cloud-icon">
          <UploadCloud size={34} />
        </div>

        <h3>Drag and drop your Excel file here</h3>
        <p>or click to browse files</p>

        <span className="excel-upload__browse">
          Browse Files
        </span>

        <div className="excel-upload__formats">
          Supported formats: XLSX, XLS · Maximum size: 20 MB
        </div>
      </div>

      {selectedFile && (
        <div className="excel-upload__file">
          <div className="excel-upload__file-icon">
            <FileSpreadsheet size={24} />
          </div>

          <div className="excel-upload__file-info">
            <strong>{selectedFile.name}</strong>
            <span>{formatFileSize(selectedFile.size)}</span>

            {isUploading && (
              <>
                <div className="excel-upload__progress">
                  <div
                    className="excel-upload__progress-bar"
                    style={{ width: `${progress}%` }}
                  />
                </div>
                <span>{progress}% uploaded</span>
              </>
            )}
          </div>

          {!isUploading && (
            <button
              type="button"
              className="excel-upload__remove"
              onClick={handleRemoveFile}
              aria-label="Remove selected file"
            >
              <X size={18} />
            </button>
          )}
        </div>
      )}

      {error && (
        <div className="excel-upload__message excel-upload__message--error">
          <AlertCircle size={18} />
          <span>{error}</span>
        </div>
      )}

      {uploadedFile && (
        <div className="excel-upload__message excel-upload__message--success">
          <CheckCircle2 size={20} />

          <div>
            <strong>File uploaded successfully!</strong>
            <p>{uploadedFile.fileName}</p>
            <small>Chat ID: {uploadedFile.chatId}</small>
          </div>
        </div>
      )}

      <button
        type="button"
        className="excel-upload__submit"
        disabled={!selectedFile || isUploading}
        onClick={handleUpload}
      >
        {isUploading ? (
          <>
            <Loader2 size={18} className="excel-upload__spinner" />
            Uploading...
          </>
        ) : (
          <>
            <UploadCloud size={18} />
            Upload Excel File
          </>
        )}
      </button>
    </div>
  );
}
