export type FormFieldType = 'Text' | 'TextArea' | 'Checkbox' | 'Select' | 'Number' | 'Date';

export const FieldTypeLabels: Record<FormFieldType, string> = {
  Text: 'سطر نصي',
  TextArea: 'نص طويل',
  Checkbox: 'خانة اختيار',
  Select: 'قائمة منسدلة',
  Number: 'رقم',
  Date: 'تاريخ',
};

export const SectionLabels: Record<string, string> = {
  SuggestionData: 'بيانات المقترح',
  SuggestionDetails: 'تفاصيل المقترح',
  Impact: 'أثر التطبيق',
};

export interface FormField {
  id: number;
  fieldKey: string;
  labelAr: string;
  labelEn: string | null;
  placeholder: string | null;
  fieldType: FormFieldType;
  section: string;
  isRequired: boolean;
  isActive: boolean;
  isSystem: boolean;
  sortOrder: number;
  options: string[];
}

export interface SaveFormFieldRequest {
  fieldKey: string;
  labelAr: string;
  labelEn?: string | null;
  placeholder?: string | null;
  fieldType: FormFieldType;
  section: string;
  isRequired: boolean;
  isActive: boolean;
  sortOrder: number;
  options?: string[];
}
