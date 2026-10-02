import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Department, DepartmentService } from '../../core/services/department.service';

/**
 * قائمة الإدارات في الإعدادات: يضيفها مدير النظام هنا، ثم تُختار من قائمة منسدلة
 * عند إضافة الموظفين أو تعديلهم — فلا تُكتب الإدارة الواحدة بأكثر من صيغة.
 */
@Component({
  selector: 'app-departments-settings',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
  <div class="mb-6">
    <div class="flex items-center justify-between mb-2.5">
      <div>
        <h2 class="text-[13px] font-extrabold text-ink">الإدارات والأقسام</h2>
        <p class="text-[11px] text-ink-faint mt-0.5">تظهر هذه الإدارات في قائمة منسدلة عند إضافة موظف أو تعديله، وفي فلاتر البحث.</p>
      </div>
    </div>
    <div class="card overflow-hidden">
      <!-- إضافة إدارة -->
      <form (ngSubmit)="add()" class="flex gap-2.5 px-5 py-4 border-b border-[#F3F5F4]">
        <input [(ngModel)]="newName" name="newName" type="text" class="field-input flex-1"
               placeholder="اسم الإدارة الجديدة، مثال: إدارة الموارد البشرية" aria-label="اسم الإدارة الجديدة">
        <button type="submit" [disabled]="busy() || !newName.trim()" class="btn btn-primary whitespace-nowrap">+ إضافة إدارة</button>
      </form>

      @if (loading()) {
        <div class="px-5 py-8 text-center text-[12.5px] text-ink-faint">جارِ التحميل...</div>
      } @else {
        @for (d of departments(); track d.id) {
          <div class="flex flex-wrap items-center gap-3 px-5 py-3 border-b border-[#F3F5F4] last:border-b-0" [class.opacity-60]="!d.isActive">
            @if (editingId() === d.id) {
              <input [(ngModel)]="editName" [name]="'edit' + d.id" type="text" class="field-input flex-1 min-w-[200px]"
                     (keydown.enter)="saveRename(d)" (keydown.escape)="editingId.set(null)" aria-label="الاسم الجديد للإدارة">
              <button (click)="saveRename(d)" [disabled]="busy()" class="btn btn-primary !py-1.5 !px-3 !text-[11px]">حفظ</button>
              <button (click)="editingId.set(null)" class="btn btn-ghost !py-1.5 !px-3 !text-[11px]">إلغاء</button>
            } @else {
              <div class="flex-1 min-w-[200px] flex items-center gap-2 flex-wrap">
                <span class="text-[13px] font-bold text-ink">{{ d.name }}</span>
                <span class="badge badge-gray">{{ d.userCount }} مستخدم</span>
                @if (!d.isActive) { <span class="badge badge-red">موقوفة</span> }
              </div>
              <div class="flex gap-1.5">
                <button (click)="startRename(d)" class="btn btn-ghost !py-1.5 !px-3 !text-[11px]">تعديل الاسم</button>
                <button (click)="toggle(d)" [disabled]="busy()" class="btn btn-ghost !py-1.5 !px-3 !text-[11px]">
                  {{ d.isActive ? 'إيقاف' : 'تفعيل' }}
                </button>
                @if (d.userCount === 0) {
                  <button (click)="remove(d)" [disabled]="busy()" class="btn btn-danger !py-1.5 !px-3 !text-[11px]">حذف</button>
                }
              </div>
            }
          </div>
        } @empty {
          <div class="px-5 py-8 text-center text-[12.5px] text-ink-faint">لا توجد إدارات بعد. أضف أول إدارة من الحقل أعلاه.</div>
        }
      }

      @if (error()) {
        <div class="mx-5 mb-4 text-[12.5px] text-red-700 bg-red-50 border border-red-100 rounded-xl px-3.5 py-2.5">{{ error() }}</div>
      }
    </div>
    <p class="text-[11px] text-ink-faint mt-2">
      تعديل اسم الإدارة ينقل موظفيها ومقترحاتها إلى الاسم الجديد تلقائياً. الإدارة المرتبطة بموظفين لا تُحذف — أوقفها لتختفي من القائمة دون المساس بسجلاتهم.
    </p>
  </div>
  `,
})
export class DepartmentsSettingsComponent implements OnInit {
  private service = inject(DepartmentService);

  departments = signal<Department[]>([]);
  loading = signal(true);
  busy = signal(false);
  error = signal<string | null>(null);
  editingId = signal<number | null>(null);
  newName = '';
  editName = '';

  ngOnInit() { this.load(); }

  load() {
    this.service.getAll().subscribe({
      next: d => { this.departments.set(d); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  add() {
    const name = this.newName.trim();
    if (!name) return;
    this.run(this.service.create(name), () => (this.newName = ''));
  }

  startRename(d: Department) {
    this.error.set(null);
    this.editName = d.name;
    this.editingId.set(d.id);
  }

  saveRename(d: Department) {
    const name = this.editName.trim();
    if (!name) { this.error.set('اسم الإدارة إلزامي.'); return; }
    this.run(this.service.update(d.id, name, d.isActive), () => this.editingId.set(null));
  }

  toggle(d: Department) { this.run(this.service.update(d.id, d.name, !d.isActive)); }

  remove(d: Department) {
    if (!confirm(`حذف الإدارة "${d.name}"؟`)) return;
    this.run(this.service.delete(d.id));
  }

  private run(obs: import('rxjs').Observable<unknown>, after?: () => void) {
    this.busy.set(true);
    this.error.set(null);
    obs.subscribe({
      next: () => { this.busy.set(false); after?.(); this.load(); },
      error: err => { this.busy.set(false); this.error.set(err?.error?.message ?? 'تعذّر تنفيذ العملية.'); },
    });
  }
}
