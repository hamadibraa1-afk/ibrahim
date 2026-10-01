export enum UserRole {
  Employee = 'Employee',
  Screener = 'Screener',
  CommitteeMember = 'CommitteeMember',
  Admin = 'Admin',
}

export enum UserStatus {
  Active = 'Active',
  Suspended = 'Suspended',
}

export const UserRoleLabels: Record<UserRole, string> = {
  [UserRole.Employee]: 'موظف',
  [UserRole.Screener]: 'موظف فرز',
  [UserRole.CommitteeMember]: 'عضو لجنة',
  [UserRole.Admin]: 'مدير النظام',
};

export interface User {
  id: number;
  userCode: string;        // الرقم الوظيفي
  arabicName: string;      // الاسم بالعربي
  englishName: string;     // الاسم بالإنجليزي
  email: string;           // البريد الإلكتروني
  role: UserRole;
  phoneNumber: string;
  department: string;      // الإدارة / القسم
  jobTitle: string;        // المسمى الوظيفي
  status: UserStatus;
  createdAt: string;
  /** المدير المباشر — أول جهة يُصعَّد إليها عند تأخر الموظف عن مهلته. */
  managerId: number | null;
  managerName: string | null;
}

export interface CreateUserRequest {
  userCode: string;
  arabicName: string;
  englishName: string;
  email: string;
  initialPassword: string;
  role: UserRole;
  phoneNumber: string;
  department: string;
  jobTitle: string;
  managerId: number | null;
}

export interface UpdateUserRequest {
  id: number;
  userCode: string;
  arabicName: string;
  englishName: string;
  email: string;
  role: UserRole;
  phoneNumber: string;
  department: string;
  jobTitle: string;
  managerId: number | null;
}

export interface LoginRequest { userCode: string; password: string; }
/** لا يحتوي الرد على رمز الجلسة — الرمز في كعكة HttpOnly لا تقرؤها الواجهة. */
export interface LoginResponse { expiresAt: string; user: User; }
export interface SessionResponse { expiresAt: string; }
export interface ChangePasswordRequest { currentPassword: string; newPassword: string; }
