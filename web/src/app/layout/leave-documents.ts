import { Component, OnDestroy, OnInit, inject, input, output, signal } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { Api } from '../core/api';
import { Backdrop } from '../core/backdrop';
import { I18n, TPipe } from '../core/i18n';
import { Ui } from '../core/ui';

interface Doc { id: string; fileName: string; contentType: string; size: number; }

/**
 * The documents behind one leave request: list, preview in place, and (for the employee while
 * the request is pending) add or remove. Files are fetched through the API, so the same access
 * rule applies here as on the server.
 */
@Component({
  selector: 'app-leave-documents',
  standalone: true,
  imports: [TPipe, Backdrop],
  template: `
    <div class="modal-back" appBackdrop (dismiss)="closed.emit()"><div class="modal wide">
      <div class="modal-head"><h2>{{ 'doc.title' | t }}</h2>
        <button class="btn sm" (click)="closed.emit()">{{ 'common.close' | t }}</button></div>
      @if (error()) { <div class="alert red">{{ error() }}</div> }

      <div class="divide-y divide-line rounded border border-line">
        @for (d of docs(); track d.id) {
          <div class="flex flex-wrap items-center gap-2 px-3 py-2 text-sm" [class.bg-raised]="viewing() === d.id">
            <span class="flex-1 break-all">{{ d.fileName }}</span>
            <span class="muted small tabular" dir="ltr">{{ kb(d.size) }}</span>
            <button class="btn sm" (click)="show(d)">{{ 'doc.view' | t }}</button>
            @if (editable()) { <button class="btn sm danger" [disabled]="busy()" (click)="remove(d)">{{ 'common.delete' | t }}</button> }
          </div>
        } @empty { <div class="px-3 py-3 muted small">{{ 'doc.none' | t }}</div> }
      </div>

      @if (editable()) {
        <div class="field mt-3"><label for="doc-add">{{ 'doc.add' | t }}</label>
          <input id="doc-add" type="file" accept=".pdf,.jpg,.jpeg,.png,application/pdf,image/jpeg,image/png" multiple
                 [disabled]="busy()" (change)="add($event)" aria-describedby="doc-hint">
          <p id="doc-hint" class="mt-1 text-xs text-muted">{{ 'doc.hint' | t }}</p></div>
      }

      @if (preview(); as p) {
        <div class="mt-4 overflow-hidden rounded-xl border border-line bg-raised">
          @if (p.isImage) { <img [src]="p.url" [alt]="p.name" class="mx-auto max-h-[70vh] w-auto"> }
          @else { <iframe [src]="p.url" [title]="p.name" class="h-[70vh] w-full"></iframe> }
        </div>
        <div class="mt-2 text-end"><a class="btn sm" [href]="p.url" [attr.download]="p.name">{{ 'doc.download' | t }}</a></div>
      }
    </div></div>`,
})
export class LeaveDocuments implements OnInit, OnDestroy {
  readonly leaveId = input.required<string>();
  readonly editable = input(false);
  readonly closed = output<void>();
  readonly changed = output<number>();

  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  private readonly i18n = inject(I18n);
  private readonly sanitizer = inject(DomSanitizer);
  readonly docs = signal<Doc[]>([]);
  readonly preview = signal<{ url: SafeResourceUrl; name: string; isImage: boolean } | null>(null);
  readonly viewing = signal<string | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  private objectUrl: string | null = null;

  ngOnInit(): void { this.load(); }
  ngOnDestroy(): void { this.release(); }

  kb(size: number): string { return size >= 1048576 ? (size / 1048576).toFixed(1) + ' MB' : Math.max(1, Math.round(size / 1024)) + ' KB'; }

  async load(): Promise<void> {
    try {
      this.docs.set(await this.api.get<Doc[]>(`leaves/${this.leaveId()}/attachments`));
      this.changed.emit(this.docs().length);
      if (!this.preview() && this.docs().length) await this.show(this.docs()[0]);
    } catch (e) { this.error.set(this.api.error(e).message); }
  }

  async show(d: Doc): Promise<void> {
    try {
      const blob = await this.api.blob(`leaves/${this.leaveId()}/attachments/${d.id}/content`);
      this.release();
      // The browser picks the viewer from this type, which the server derived from the file itself.
      this.objectUrl = URL.createObjectURL(new Blob([blob], { type: d.contentType }));
      this.preview.set({ url: this.sanitizer.bypassSecurityTrustResourceUrl(this.objectUrl), name: d.fileName, isImage: d.contentType.startsWith('image/') });
      this.viewing.set(d.id);
    } catch (e) { this.error.set(this.api.error(e).message); }
  }

  async add(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    if (!files.length) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      for (const file of files) await this.api.upload(`me/leaves/${this.leaveId()}/attachments`, file);
      this.ui.ok(this.i18n.t('common.saved'));
    } catch (e) { this.error.set(this.api.error(e).message); }
    finally { input.value = ''; this.busy.set(false); await this.load(); }
  }

  async remove(d: Doc): Promise<void> {
    if (!(await this.ui.confirm(this.i18n.t('common.delete') + ': ' + d.fileName, this.i18n.t('common.deleteConfirm'), true))) return;
    this.busy.set(true);
    try {
      await this.api.delete(`me/leaves/${this.leaveId()}/attachments/${d.id}`);
      if (this.viewing() === d.id) { this.release(); this.preview.set(null); this.viewing.set(null); }
      await this.load();
    } catch (e) { this.error.set(this.api.error(e).message); } finally { this.busy.set(false); }
  }

  private release(): void {
    if (this.objectUrl) URL.revokeObjectURL(this.objectUrl);
    this.objectUrl = null;
  }
}
