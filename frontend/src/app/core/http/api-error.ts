import { HttpErrorResponse } from '@angular/common/http';

/** RFC 9457 problem details, as returned by the API for every error. */
export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  code?: string;
  errors?: Record<string, string[]>;
}

/** A transport-agnostic error the UI can display. */
export interface ApiError {
  status: number;
  message: string;
  code?: string;
  fieldErrors: Record<string, string[]>;
}

/**
 * No answer, or a server error: the request may or may not have been applied, and only the server can tell.
 * (A 4xx answer means it was refused.)
 */
export function isOutcomeUnknown(error: ApiError): boolean {
  return error.status < 400 || error.status >= 500;
}

export function toApiError(error: unknown): ApiError {
  if (!(error instanceof HttpErrorResponse)) {
    return { status: -1, message: 'Something went wrong.', fieldErrors: {} };
  }

  if (error.status === 0) {
    return {
      status: 0,
      message: 'Cannot reach the server. Check your connection and try again.',
      fieldErrors: {},
    };
  }

  const problem = (
    typeof error.error === 'object' && error.error !== null ? error.error : {}
  ) as ProblemDetails;
  return {
    status: error.status,
    message: problem.detail ?? problem.title ?? `Request failed (${error.status}).`,
    code: problem.code,
    fieldErrors: problem.errors ?? {},
  };
}
