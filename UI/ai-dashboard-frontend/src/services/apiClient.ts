import axios from "axios";
import { appConfig } from "../config/appConfig";

const apiClient = axios.create({
  baseURL: appConfig.apiBaseUrl,
  headers: {
    Accept: "application/json",
  },
});

export default apiClient;