import { Injectable, signal } from '@angular/core';

export interface Toast { id: number; text: string; kind: 'ok' | 'error'; }
interface DialogState { title: string; message?: string; input?: { label: string; required: boolean; value: string }; resolve: (v: any) => void; danger?: boolean; }

/** Toasts and modal confirm/prompt dialogs (rendered once in AppComponent). */
@Injectable({ providedIn: 'root' })
export class Ui {
  readonly toasts = signal<Toast[]>([]);
  readonly dialog = signal<DialogState | null>(null);
  private seq = 0;

  ok(text: string): void { this.push(text, 'ok'); }
  error(text: string): void { this.push(text, 'error'); }

  confirm(title: string, message?: string, danger = false): Promise<boolean> {
    return new Promise(resolve => this.dialog.set({ title, message, resolve, danger }));
  }

  prompt(title: string, label: string, required = true): Promise<string | null> {
    return new Promise(resolve => this.dialog.set({ title, input: { label, required, value: '' }, resolve }));
  }

  close(result: any): void { const d = this.dialog(); this.dialog.set(null); d?.resolve(result); }

  private push(text: string, kind: Toast['kind']): void {
    const id = ++this.seq;
    this.toasts.update(t => [...t, { id, text, kind }]);
    setTimeout(() => this.toasts.update(t => t.filter(x => x.id !== id)), kind === 'error' ? 6000 : 3000);
  }
}
