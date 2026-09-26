import { Directive, HostListener, output } from '@angular/core';

/**
 * Dismisses a dialog only when the press starts AND ends on the backdrop itself.
 * Without this, selecting text inside a field and releasing the button outside the
 * dialog counts as a backdrop click and throws away whatever the user was typing.
 * Escape also dismisses, which is what people expect from a dialog.
 */
@Directive({ selector: '[appBackdrop]', standalone: true })
export class Backdrop {
  readonly dismiss = output<void>();
  private armed = false;

  @HostListener('mousedown', ['$event'])
  onDown(event: MouseEvent): void { this.armed = event.target === event.currentTarget; }

  @HostListener('click', ['$event'])
  onClick(event: MouseEvent): void {
    const onBackdrop = this.armed && event.target === event.currentTarget;
    this.armed = false;
    if (onBackdrop) this.dismiss.emit();
  }

  @HostListener('document:keydown.escape')
  onEscape(): void { this.dismiss.emit(); }
}
