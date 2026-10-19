import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { CONFIRM, UNSAVED_CHANGES_MESSAGE, unsavedChangesGuard } from './unsaved-changes.guard';

describe('unsavedChangesGuard', () => {
  function leave(hasUnsavedChanges: boolean, answer = false) {
    const asked: string[] = [];
    const confirm = (message: string) => {
      asked.push(message);
      return answer;
    };
    TestBed.configureTestingModule({ providers: [{ provide: CONFIRM, useValue: confirm }] });

    const allowed = TestBed.runInInjectionContext(() =>
      unsavedChangesGuard(
        { hasUnsavedChanges: () => hasUnsavedChanges },
        {} as ActivatedRouteSnapshot,
        {} as RouterStateSnapshot,
        {} as RouterStateSnapshot,
      ),
    );
    return { allowed, asked };
  }

  it('lets the user leave a page with nothing to lose without asking', () => {
    expect(leave(false)).toEqual({ allowed: true, asked: [] });
  });

  it('asks before unsaved changes are lost, and stays when the user says no', () => {
    expect(leave(true, false)).toEqual({ allowed: false, asked: [UNSAVED_CHANGES_MESSAGE] });
  });

  it('leaves when the user agrees to lose the changes', () => {
    expect(leave(true, true).allowed).toBe(true);
  });
});
