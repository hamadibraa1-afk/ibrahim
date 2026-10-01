import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Attachment } from '../models/proposal.model';

@Injectable({ providedIn: 'root' })
export class AttachmentService {
  private http = inject(HttpClient);

  private base(proposalId: number) {
    return `${environment.apiUrl}/proposals/${proposalId}/attachments`;
  }

  list(proposalId: number): Observable<Attachment[]> {
    return this.http.get<Attachment[]>(this.base(proposalId));
  }

  upload(proposalId: number, files: File[]): Observable<Attachment[]> {
    const formData = new FormData();
    files.forEach(f => formData.append('files', f, f.name));
    return this.http.post<Attachment[]>(this.base(proposalId), formData);
  }

  delete(proposalId: number, attachmentId: number): Observable<void> {
    return this.http.delete<void>(`${this.base(proposalId)}/${attachmentId}`);
  }

  /**
   * التنزيل يتم عبر HttpClient (وليس رابط مباشر) حتى تمر ترويسة X-User-Id
   * عبر الـ interceptor ويعمل التحقق من الصلاحيات على الخادم.
   */
  download(proposalId: number, att: Attachment): void {
    this.http
      .get(`${this.base(proposalId)}/${att.id}/download`, { responseType: 'blob' })
      .subscribe(blob => this.saveBlob(blob, att.fileName));
  }

  /** فتح المرفق داخل تبويب جديد (للـ PDF والصور). */
  preview(proposalId: number, att: Attachment): void {
    this.http
      .get(`${this.base(proposalId)}/${att.id}/preview`, { responseType: 'blob' })
      .subscribe(blob => {
        const url = URL.createObjectURL(blob);
        window.open(url, '_blank');
        setTimeout(() => URL.revokeObjectURL(url), 60_000);
      });
  }

  /** هل يمكن عرض هذا النوع داخل المتصفح مباشرة؟ */
  isPreviewable(att: Attachment): boolean {
    return att.contentType === 'application/pdf' || att.contentType.startsWith('image/');
  }

  private saveBlob(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  }
}
