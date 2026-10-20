'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useEffect, useId, useState, type FormEvent } from 'react';
import { Link, useRouter } from '@/i18n/navigation';
import { api } from '@/lib/api/client';
import type { ChallengeResponse, ContactType, NicknameSuggestion, SignupRequest } from '@/lib/api/types';
import { Button } from '@/components/ui/Button';
import { Notice } from '@/components/ui/Notice';
import { TextField } from '@/components/ui/TextField';
import { MIN_PASSWORD_LENGTH, isValidEmail, isValidPhone, normalizeEmail, normalizePhone } from '@/components/auth/contact';
import { fieldErrorsOf, problemText } from '@/components/auth/errorText';
import { PasswordField } from '@/components/auth/PasswordField';
import { FLOW_KEYS, isEligibilityState, isSignupDraft, readFlow, writeFlow, type SignupState } from './flowStorage';
import { Segmented } from './Segmented';

type Errors = Partial<Record<'nickname' | 'contact' | 'password' | 'form', string>>;

/** P02 Signup, step 1 of 4: nickname, contact for verification, password. */
export function SignupForm({ restarted }: { restarted?: boolean }) {
  const t = useTranslations('signup');
  const te = useTranslations('errors');
  const locale = useLocale();
  const router = useRouter();
  const contactLabelId = useId();
  const [token, setToken] = useState<string | null>(null);
  const [nickname, setNickname] = useState('');
  const [contactType, setContactType] = useState<ContactType>('phone');
  const [contact, setContact] = useState('');
  const [password, setPassword] = useState('');
  const [errors, setErrors] = useState<Errors>({});
  const [busy, setBusy] = useState(false);
  const [suggesting, setSuggesting] = useState(false);
  const [suggestError, setSuggestError] = useState(false);

  async function suggest() {
    setSuggesting(true);
    setSuggestError(false);
    try {
      const { nickname: next } = await api<NicknameSuggestion>('auth/nickname');
      setNickname(next);
      setErrors((e) => ({ ...e, nickname: undefined }));
    } catch {
      setSuggestError(true);
    } finally {
      setSuggesting(false);
    }
  }

  useEffect(() => {
    const eligibility = readFlow(FLOW_KEYS.eligibility, isEligibilityState);
    if (!eligibility) {
      router.replace('/start/eligibility?restart=1');
      return;
    }
    setToken(eligibility.eligibilityToken);
    // Coming back from the verify step ("change number") keeps what was typed, except the password.
    const draft = readFlow(FLOW_KEYS.signup, isSignupDraft);
    if (draft) {
      setNickname(draft.nickname);
      setContactType(draft.contactType);
      setContact(draft.contact);
    }
    if (!draft?.nickname) void suggest();
    // Runs once on arrival.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function validate(): Errors {
    const next: Errors = {};
    if (!nickname.trim()) next.nickname = t('nicknameRequired');
    if (contactType === 'phone' && !isValidPhone(contact)) next.contact = t('phoneInvalid');
    if (contactType === 'email' && !isValidEmail(contact)) next.contact = t('emailInvalid');
    if (password.length < MIN_PASSWORD_LENGTH) next.password = t('passwordTooShort');
    return next;
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    const next = validate();
    setErrors(next);
    if (Object.keys(next).length > 0 || !token) return;

    const normalized = contactType === 'phone' ? normalizePhone(contact) : normalizeEmail(contact);
    const body: SignupRequest = { eligibilityToken: token, nickname: nickname.trim(), contactType, contact: normalized, password, language: locale === 'en' ? 'en' : 'ar' };
    setBusy(true);
    try {
      const challenge = await api<ChallengeResponse>('auth/signup', { method: 'POST', body });
      const state: SignupState = {
        nickname: body.nickname,
        contactType,
        contact: normalized,
        challengeId: challenge.challengeId,
        maskedContact: challenge.maskedContact,
        resendAfterSeconds: challenge.resendAfterSeconds,
        sentAt: Date.now(),
      };
      if (!writeFlow(FLOW_KEYS.signup, state)) {
        setErrors({ form: t('storageBlocked') });
        return;
      }
      router.push('/start/verify');
    } catch (err) {
      const fields = fieldErrorsOf(err);
      const mapped: Errors = { nickname: fields.nickname, contact: fields.contact, password: fields.password };
      const hasField = Boolean(mapped.nickname || mapped.contact || mapped.password);
      setErrors({ ...mapped, form: hasField ? undefined : problemText(err, te('network')) });
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} noValidate className="flex flex-col gap-5">
      {restarted ? <Notice tone="warning">{t('restartNotice')}</Notice> : null}

      <div className="flex items-end gap-3">
        <div className="min-w-0 flex-1">
          <TextField
            label={t('nickname')}
            value={nickname}
            onChange={(e) => setNickname(e.target.value)}
            error={errors.nickname}
            autoComplete="nickname"
            maxLength={40}
            name="nickname"
          />
        </div>
        <Button variant="secondary" compact onClick={suggest} busy={suggesting} className="mb-0.5 min-h-[48px] shrink-0">
          {t('newName')}
        </Button>
      </div>
      {suggestError ? <p className="-mt-3 text-sm text-muted">{t('nicknameError')}</p> : null}

      <div className="flex flex-col gap-1.5">
        <p id={contactLabelId} className="text-sm font-semibold">
          {t('contactMethod')}
        </p>
        <Segmented
          labelledBy={contactLabelId}
          value={contactType}
          onChange={(v) => {
            setContactType(v);
            setContact('');
            setErrors((e) => ({ ...e, contact: undefined }));
          }}
          options={[
            { value: 'phone', label: t('phone') },
            { value: 'email', label: t('email') },
          ]}
        />
        <TextField
          label={<span className="sr-only">{contactType === 'phone' ? t('phone') : t('email')}</span>}
          value={contact}
          onChange={(e) => setContact(contactType === 'phone' ? e.target.value.replace(/[^\d٠-٩۰-۹+\s-]/g, '') : e.target.value)}
          dir="ltr"
          type={contactType === 'phone' ? 'tel' : 'email'}
          inputMode={contactType === 'phone' ? 'tel' : 'email'}
          autoComplete={contactType === 'phone' ? 'tel' : 'email'}
          placeholder={contactType === 'phone' ? '05X XXX XXXX' : 'name@example.com'}
          hint={t('contactHint')}
          error={errors.contact}
          name="contact"
          className="text-start"
        />
      </div>

      <PasswordField label={t('password')} value={password} onChange={setPassword} autoComplete="new-password" error={errors.password} showStrength name="password" />

      {errors.form ? <Notice tone="danger">{errors.form}</Notice> : null}

      <div className="mt-6 flex flex-col items-center gap-3">
        <Button type="submit" full busy={busy}>
          {t('submit')}
        </Button>
        <p className="text-sm">
          {t('haveAccount')}{' '}
          <Link href="/login" className="font-semibold text-primary underline underline-offset-4">
            {t('signIn')}
          </Link>
        </p>
      </div>
    </form>
  );
}
