import type { ChangeEvent } from 'react';
import { ActionButton } from './ActionButton';

export interface BlockStatus {
  label: string;
  tone?: 'neutral' | 'success' | 'warning' | 'info';
}

export interface BlockInput {
  id: string;
  label: string;
  placeholder?: string;
  value: string;
  onChange: (value: string) => void;
}

export interface BlockAction {
  label: string;
  onClick: () => void;
  disabled?: boolean;
  ariaLabel?: string;
}

interface ContentBlockProps {
  title: string;
  description?: string;
  status?: BlockStatus;
  input?: BlockInput;
  action?: BlockAction;
}

const statusTones: Record<NonNullable<BlockStatus['tone']>, string> = {
  neutral: 'bg-slate-100 text-slate-700 border-slate-200',
  success: 'bg-emerald-50 text-emerald-700 border-emerald-200',
  warning: 'bg-amber-50 text-amber-800 border-amber-200',
  info: 'bg-sky-50 text-sky-700 border-sky-200',
};

/** Один блок вертикального списка: заголовок, описание, поле ввода, статус, кнопка. */
export function ContentBlock({ title, description, status, input, action }: ContentBlockProps) {
  const handleInput = (event: ChangeEvent<HTMLInputElement>) => {
    input?.onChange(event.target.value);
  };

  return (
    <section
      aria-label={title}
      className="rounded-xl border border-slate-200 bg-slate-50/60 p-5"
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <h3 className="text-base font-semibold text-slate-900">{title}</h3>
        {status && (
          <span
            role="status"
            className={`inline-flex shrink-0 items-center rounded-full border px-3 py-1 text-xs font-medium ${statusTones[status.tone ?? 'neutral']}`}
          >
            {status.label}
          </span>
        )}
      </div>

      {description && <p className="mt-2 text-sm leading-relaxed text-slate-600">{description}</p>}

      {input && (
        <div className="mt-4">
          <label htmlFor={input.id} className="mb-1.5 block text-sm font-medium text-slate-700">
            {input.label}
          </label>
          <input
            id={input.id}
            type="text"
            value={input.value}
            placeholder={input.placeholder}
            onChange={handleInput}
            className="w-full rounded-lg border border-slate-300 bg-white px-3.5 py-2.5 text-sm text-slate-900 placeholder:text-slate-400 hover:border-slate-400 focus:border-indigo-600 focus:outline-2 focus:outline-offset-1 focus:outline-indigo-600"
          />
        </div>
      )}

      {action && (
        <div className="mt-4">
          <ActionButton
            variant="secondary"
            onClick={action.onClick}
            disabled={action.disabled}
            aria-label={action.ariaLabel ?? action.label}
          >
            {action.label}
          </ActionButton>
        </div>
      )}
    </section>
  );
}
