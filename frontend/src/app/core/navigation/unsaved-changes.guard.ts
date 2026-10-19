import { DOCUMENT } from '@angular/common';
import { InjectionToken, inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';

/** A page that can hold changes the user has not saved yet. */
export interface HasUnsavedChanges {
  hasUnsavedChanges: () => boolean;
}

/**
 * Asks the user a yes/no question. The browser's own dialog by default: leaving a page is a moment the
 * browser owns (closing the tab can only use its dialog), and it blocks navigation until answered.
 * A token so tests can answer it.
 */
export const CONFIRM = new InjectionToken<(message: string) => boolean>('CONFIRM', {
  providedIn: 'root',
  factory: () => {
    const window = inject(DOCUMENT).defaultView;
    return (message) => window?.confirm(message) ?? true;
  },
});

export const UNSAVED_CHANGES_MESSAGE = 'You have unsaved changes. Leave this page and lose them?';

/** Lets the user leave a page with unsaved changes only after confirming that they want to lose them. */
export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = (page) =>
  !page.hasUnsavedChanges() || inject(CONFIRM)(UNSAVED_CHANGES_MESSAGE);
