import type { ReactNode } from 'react';
import { ActionButton } from './ActionButton';

interface MainPanelProps {
  title: string;
  subtitle?: string;
  children: ReactNode;
  /** Текст заметной кнопки внизу. Легко заменить. */
  mainActionLabel: string;
  /** Логика главной кнопки. Подключи сюда своё действие. */
  onMainAction: () => void;
  mainActionDisabled?: boolean;
}

/** Большая левая панель: заголовок, вертикальный список блоков, главное действие. */
export function MainPanel({
  title,
  subtitle,
  children,
  mainActionLabel,
  onMainAction,
  mainActionDisabled = false,
}: MainPanelProps) {
  return (
    <section
      aria-label={title}
      className="flex min-h-[480px] min-w-0 flex-col rounded-2xl border border-slate-200 bg-white p-6 shadow-sm sm:p-8"
    >
      <header>
        <h2 className="text-2xl font-bold tracking-tight text-slate-900">{title}</h2>
        {subtitle && <p className="mt-1.5 text-sm text-slate-500">{subtitle}</p>}
      </header>

      <div className="mt-6 flex flex-col gap-4">{children}</div>

      <div className="mt-8 border-t border-slate-100 pt-6">
        <ActionButton
          variant="primary"
          fullWidth
          onClick={onMainAction}
          disabled={mainActionDisabled}
          aria-label={mainActionLabel}
        >
          {mainActionLabel}
        </ActionButton>
      </div>
    </section>
  );
}
