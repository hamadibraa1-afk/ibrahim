import { Component, Input, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AttachmentService } from '../services/attachment.service';
import { Attachment, formatFileSize } from '../models/proposal.model';

/**
 * قائمة المرفقات مع أزرار العرض والتنزيل.
 * تُستخدم في شاشات الفرز واللجنة وتفاصيل المقترح.
 */
@Component({
  selector: 'app-attachment-list',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (!attachments.length) {
      <div class="text-[11.5px] text-gray-400">لا توجد مرفقات لهذا المقترح.</div>
    } @else {
      <div class="space-y-1.5">
        @for (a of attachments; track a.id) {
          <div class="flex items-center justify-between gap-3 bg-gray-50 border border-gray-100 rounded-lg px-3 py-2">
            <div class="flex items-center gap-2.5 min-w-0">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#6B7280" stroke-width="2" class="shrink-0">
                <path d="M14 3H7a2 2 0 00-2 2v14a2 2 0 002 2h10a2 2 0 002-2V8z"/><path d="M14 3v5h5"/>
              </svg>
              <div class="min-w-0">
                <div class="text-[12px] font-bold text-gray-700 truncate">{{ a.fileName }}</div>
                <div class="text-[10px] text-gray-400">{{ formatSize(a.sizeBytes) }}</div>
              </div>
            </div>
            <div class="flex gap-1.5 shrink-0">
              @if (attachmentService.isPreviewable(a)) {
                <button type="button" (click)="preview(a)"
                        class="text-[11px] text-gray-600 bg-white border border-gray-200 hover:bg-gray-100 px-2.5 py-1.5 rounded-md">
                  عرض
                </button>
              }
              <button type="button" (click)="download(a)"
                      class="text-[11px] text-white bg-teal hover:bg-teal-light px-2.5 py-1.5 rounded-md font-bold">
                تنزيل
              </button>
              @if (allowDelete) {
                <button type="button" (click)="remove(a)"
                        class="text-[11px] text-red-600 bg-red-50 hover:bg-red-100 px-2.5 py-1.5 rounded-md">
                  حذف
                </button>
              }
            </div>
          </div>
        }
      </div>
    }
  `,
})
export class AttachmentListComponent {
  @Input({ required: true }) proposalId!: number;
  @Input({ required: true }) attachments: Attachment[] = [];
  @Input() allowDelete = false;

  attachmentService = inject(AttachmentService);
  formatSize = formatFileSize;

  preview(a: Attachment) {
    this.attachmentService.preview(this.proposalId, a);
  }

  download(a: Attachment) {
    this.attachmentService.download(this.proposalId, a);
  }

  remove(a: Attachment) {
    if (!confirm(`حذف المرفق "${a.fileName}"؟`)) return;
    this.attachmentService.delete(this.proposalId, a.id).subscribe(() => {
      this.attachments = this.attachments.filter(x => x.id !== a.id);
    });
  }
}
