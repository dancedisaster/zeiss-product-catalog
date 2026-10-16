import { HttpErrorResponse } from '@angular/common/http';
import { toApiError } from './api-error';

describe('toApiError', () => {
  it('reads detail, code and field errors from RFC 9457 problem details', () => {
    const error = new HttpErrorResponse({
      status: 400,
      error: {
        title: 'Validation failed',
        detail: 'One or more validation errors occurred.',
        code: 'validation_failed',
        errors: { sku: ['Invalid'] },
      },
    });

    expect(toApiError(error)).toEqual({
      status: 400,
      message: 'One or more validation errors occurred.',
      code: 'validation_failed',
      fieldErrors: { sku: ['Invalid'] },
    });
  });

  it('explains network failures (status 0)', () => {
    const apiError = toApiError(new HttpErrorResponse({ status: 0 }));

    expect(apiError.status).toBe(0);
    expect(apiError.message).toContain('Cannot reach the server');
  });

  it('falls back to a generic message for non-HTTP errors', () => {
    expect(toApiError(new Error('boom')).message).toBe('Something went wrong.');
  });
});
