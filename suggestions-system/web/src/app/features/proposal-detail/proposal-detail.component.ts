import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ProposalService } from '../../core/services/proposal.service';
import { UserService } from '../../core/services/user.service';
import { AuthService } from '../../core/services/auth.service';
import { AttachmentListComponent } from '../../core/components/attachment-list.component';
import { AuditTimelineComponent } from '../../core/components/audit-timeline.component';
import { StatusBadgeComponent } from '../../core/components/status-badge.component';
import {
  ClassificationLabels, selectedImpacts, NotApplicableReason, NotApplicableReasonLabels,
  Proposal, ProposalClassification, ProposalStatus,
} from '../../core/models/proposal.model';
import { User, UserRole } from '../../core/models/user.model';

/**
 * صفحة تفاصيل مقترح واحد — نقطة العمل المركزية.
 * تعرض الإجراءات المتاحة حسب دور المستخدم وحالة المقترح فقط.
 */
@Component({
  selector: 'app-proposal-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, AttachmentListComponent, AuditTimelineComponent, StatusBadgeComponent],
  templateUrl: './proposal-detail.component.html',
})
export class ProposalDetailComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private location = inject(Location);
  private proposalService = inject(ProposalService);
  private userService = inject(UserService);
  auth = inject(AuthService);

  readonly ProposalStatus = ProposalStatus;
  readonly UserRole = UserRole;
  readonly ProposalClassification = ProposalClassification;
  readonly ClassificationLabels = ClassificationLabels;
  readonly NotApplicableReason = NotApplicableReason;
  readonly NotApplicableReasonLabels = NotApplicableReasonLabels;
  readonly classificationOptions = Object.values(ProposalClassification);
  readonly reasonOptions = Object.values(NotApplicableReason);

  proposal = signal<Proposal | null>(null);
  users = signal<User[]>([]);
  loading = signal(true);
  busy = signal(false);
  errorMessage = signal<string | null>(null);
  successMessage = signal<string | null>(null);

  // نماذج الإجراءات
  screenNotes = '';
  classification: ProposalClassification | null = null;
  notApplicableReason: NotApplicableReason | null = null;
  study = '';
  recommendation = '';
  execNotes = '';
  ownerId: number | null = null;
  escalationNote = '';

  ngOnInit() {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.load(id);
    if (this.auth.role() === UserRole.Admin) {
      this.userService.getAll().subscribe(u => this.users.set(u));
    }
  }

  load(id: number) {
    this.loading.set(true);
    this.proposalService.getById(id).subscribe({
      next: p => {
        this.proposal.set(p);
        this.classification = p.classification;
        this.notApplicableReason = p.notApplicableReasonValue;
        this.study = p.committeeStudy ?? '';
        this.recommendation = p.committeeRecommendation ?? '';
        this.ownerId = p.ownerId;
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  goBack() { this.location.back(); }

  activeImpacts(p: Proposal): string[] { return selectedImpacts(p); }

  hoursLeft(p: Proposal): number {
    return Math.round((new Date(p.slaDueAt).getTime() - Date.now()) / 3_600_000);
  }

  isOverdue(p: Proposal): boolean {
    return this.hoursLeft(p) < 0 && p.status !== ProposalStatus.Accepted && p.status !== ProposalStatus.Rejected;
  }

  // ---- صلاحيات الإجراءات ----
  get role() { return this.auth.role(); }
  get isMine() { return this.proposal()?.submitterId === this.auth.currentUser()?.id; }

  canScreen(): boolean {
    const p = this.proposal();
    return !!p && (this.role === UserRole.Screener || this.role === UserRole.Admin)
      && (p.status === ProposalStatus.Submitted || p.status === ProposalStatus.UnderScreening);
  }
  canStudy(): boolean {
    const p = this.proposal();
    return !!p && (this.role === UserRole.CommitteeMember || this.role === UserRole.Admin)
      && p.status === ProposalStatus.WithCommittee;
  }
  canDecide(): boolean {
    const p = this.proposal();
    return !!p && this.role === UserRole.Admin && p.status === ProposalStatus.PendingExecutiveDecision;
  }
  canEscalate(): boolean {
    const p = this.proposal();
    return !!p && (this.role === UserRole.Screener || this.role === UserRole.Admin) && this.isOverdue(p);
  }
  canEdit(): boolean {
    const p = this.proposal();
    return !!p && this.isMine && (p.status === ProposalStatus.Submitted || p.status === ProposalStatus.ReturnedForEdit);
  }
  get myVote() {
    const uid = this.auth.currentUser()?.id;
    return this.proposal()?.committeeVotes.find(v => v.memberId === uid) ?? null;
  }

  private handle(obs: any, successMsg: string, reload = true) {
    this.busy.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);
    obs.subscribe({
      next: (updated: Proposal) => {
        this.busy.set(false);
        this.successMessage.set(successMsg);
        if (reload) this.load(updated?.id ?? this.proposal()!.id);
      },
      error: (err: any) => {
        this.busy.set(false);
        this.errorMessage.set(err?.error?.message ?? 'تعذّر تنفيذ الإجراء.');
      },
    });
  }

  screen(action: 'Approve' | 'ReturnForEdit' | 'Reject') {
    const p = this.proposal(); if (!p) return;
    if (action !== 'Approve' && !this.screenNotes.trim()) {
      this.errorMessage.set(action === 'Reject' ? 'الرجاء ذكر سبب الرفض.' : 'الرجاء إضافة ملاحظات للموظف.');
      return;
    }
    const msg = action === 'Approve' ? 'تمت الإحالة للجنة.'
      : action === 'ReturnForEdit' ? 'أُعيد المقترح للموظف للتعديل.' : 'تم رفض المقترح.';
    this.handle(this.proposalService.screen(p.id, { action, notes: this.screenNotes || undefined }), msg);
  }

  saveStudy() {
    const p = this.proposal(); if (!p) return;
    if (!this.classification) { this.errorMessage.set('الرجاء تحديد تصنيف المقترح.'); return; }
    if (this.classification === ProposalClassification.NotApplicable && !this.notApplicableReason) {
      this.errorMessage.set('الرجاء تحديد سبب عدم قابلية التطبيق.'); return;
    }
    if (!this.study.trim() || !this.recommendation.trim()) {
      this.errorMessage.set('الرجاء تعبئة الدراسة والتوصية.'); return;
    }
    this.handle(this.proposalService.saveCommitteeStudy(p.id, {
      classification: this.classification,
      notApplicableReasonValue: this.classification === ProposalClassification.NotApplicable ? this.notApplicableReason : null,
      committeeStudy: this.study,
      committeeRecommendation: this.recommendation,
    }), 'تم حفظ الدراسة والتوصية.');
  }

  sign() {
    const p = this.proposal(); const uid = this.auth.currentUser()?.id;
    if (!p || !uid) return;
    this.handle(this.proposalService.committeeSign(p.id, uid), 'تم تسجيل توقيعك.');
  }

  decide(decision: 'Accepted' | 'Rejected') {
    const p = this.proposal(); if (!p) return;
    if (decision === 'Rejected' && !this.execNotes.trim()) {
      this.errorMessage.set('الرجاء ذكر سبب عدم الموافقة.'); return;
    }
    this.handle(this.proposalService.executiveDecision(p.id, { decision, notes: this.execNotes || undefined }),
      decision === 'Accepted' ? 'تمت الموافقة على المقترح.' : 'تم رفض المقترح.');
  }

  assignOwner() {
    const p = this.proposal(); if (!p || !this.ownerId) return;
    this.handle(this.proposalService.assignOwner(p.id, { ownerId: this.ownerId }), 'تم تعيين مسؤول التنفيذ.');
  }

  escalate() {
    const p = this.proposal(); if (!p) return;
    if (!this.escalationNote.trim()) { this.errorMessage.set('الرجاء كتابة سبب التصعيد.'); return; }
    this.handle(this.proposalService.escalate(p.id, this.escalationNote), 'تم تصعيد المقترح لمدير النظام.');
  }

  openForm() { const p = this.proposal(); if (p) this.proposalService.openOfficialForm(p.id); }
  openMinutes() { const p = this.proposal(); if (p) this.proposalService.openMinutes(p.id); }
  openCertificate() { const p = this.proposal(); if (p) this.proposalService.openCertificate(p.id); }
}
