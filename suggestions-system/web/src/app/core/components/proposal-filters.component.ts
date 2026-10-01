import { Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { UserService } from '../services/user.service';
import {
  ClassificationLabels, ProposalClassification, ProposalQuery,
  ProposalStatus, ProposalStatusLabels,
} from '../models/proposal.model';

/**
 * شريط فلاتر موحّد يُستخدم في كل قوائم المقترحات.
 * يبثّ التغييرات عبر filtersChange ليعيد الأب تحميل البيانات.
 */
@Component({
  selector: 'app-proposal-filters',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './proposal-filters.component.html',
})
export class ProposalFiltersComponent {
  private userService = inject(UserService);

  /** إخفاء فلتر الحالة في الصفحات المخصصة لحالة واحدة. */
  @Input() showStatus = true;
  @Input() showClassification = false;
  @Input() showOverdue = true;

  @Output() filtersChange = new EventEmitter<ProposalQuery>();

  readonly ProposalStatusLabels = ProposalStatusLabels;
  readonly ClassificationLabels = ClassificationLabels;
  readonly statusOptions = Object.values(ProposalStatus);
  readonly classificationOptions = Object.values(ProposalClassification);

  departments = signal<string[]>([]);
  expanded = signal(false);

  search = '';
  status = '';
  department = '';
  classification = '';
  overdue = false;
  from = '';
  to = '';
  sort: ProposalQuery['sort'] = 'newest';

  private debounce?: ReturnType<typeof setTimeout>;

  ngOnInit() {
    this.userService.getDepartments().subscribe({
      next: d => this.departments.set(d),
      error: () => {},
    });
  }

  get activeCount(): number {
    return [this.search, this.status, this.department, this.classification, this.from, this.to]
      .filter(v => !!v).length + (this.overdue ? 1 : 0);
  }

  /** البحث النصي يُؤخَّر قليلاً حتى لا يُرسل طلباً مع كل حرف. */
  onSearchInput() {
    if (this.debounce) clearTimeout(this.debounce);
    this.debounce = setTimeout(() => this.emit(), 350);
  }

  emit() {
    this.filtersChange.emit({
      search: this.search || undefined,
      status: this.status || undefined,
      department: this.department || undefined,
      classification: this.classification || undefined,
      overdue: this.overdue || undefined,
      from: this.from || undefined,
      to: this.to || undefined,
      sort: this.sort,
    });
  }

  reset() {
    this.search = ''; this.status = ''; this.department = '';
    this.classification = ''; this.overdue = false; this.from = ''; this.to = '';
    this.sort = 'newest';
    this.emit();
  }
}
