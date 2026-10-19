import { TestBed } from '@angular/core/testing';
import { Paginator } from './paginator';

describe('Paginator', () => {
  function render(page: number, total: number) {
    const fixture = TestBed.createComponent(Paginator);
    fixture.componentRef.setInput('page', page);
    fixture.componentRef.setInput('pageSize', 10);
    fixture.componentRef.setInput('total', total);
    const emitted: number[] = [];
    fixture.componentInstance.pageChange.subscribe((next) => emitted.push(next));
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const button = (label: string) =>
      [...element.querySelectorAll('button')].find((b) => b.textContent?.trim() === label)!;
    const summary = () => element.querySelector('.paginator__summary')?.textContent?.trim();
    return { element, emitted, button, summary };
  }

  it('says which products are shown and cannot go back from the first page', () => {
    const { element, button, summary } = render(1, 23);

    expect(summary()).toBe('Showing 1–10 of 23');
    expect(element.textContent).toContain('Page 1 of 3');
    expect(button('Previous').disabled).toBe(true);
    expect(button('Next').disabled).toBe(false);
  });

  it('cannot go forward from the last page, which may be partly filled', () => {
    const { button, summary } = render(3, 23);

    expect(summary()).toBe('Showing 21–23 of 23');
    expect(button('Next').disabled).toBe(true);
  });

  it('asks for the neighbouring pages', () => {
    const { emitted, button } = render(2, 23);

    button('Previous').click();
    button('Next').click();

    expect(emitted).toEqual([1, 3]);
  });

  it('shows only the count when everything fits on one page', () => {
    const { element, summary } = render(1, 7);

    expect(summary()).toBe('Showing 1–7 of 7');
    expect(element.querySelector('button')).toBeNull();
  });
});
