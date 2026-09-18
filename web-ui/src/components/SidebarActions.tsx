import type { SidebarAction } from '../config/actions';
import { ActionIcon } from './icons';

interface SidebarActionsProps {
  title?: string;
  actions: SidebarAction[];
  /** Id активной (выбранной) кнопки. */
  activeId: string | null;
  /** Вызывается при клике. Здесь же можно вызвать action.onClick. */
  onSelect: (action: SidebarAction) => void;
}

/** Правая колонка: вертикальный список одинаковых карточек-кнопок. */
export function SidebarActions({ title = 'Действия', actions, activeId, onSelect }: SidebarActionsProps) {
  return (
    <nav aria-label={title} className="min-w-0">
      <h2 className="mb-3 px-1 text-sm font-semibold uppercase tracking-wider text-slate-500">
        {title}
      </h2>
      <ul className="flex flex-col gap-3">
        {actions.map((action) => {
          const isActive = action.id === activeId;
          const isDisabled = action.disabled === true;
          return (
            <li key={action.id}>
              <button
                type="button"
                onClick={() => onSelect(action)}
                disabled={isDisabled}
                aria-pressed={isActive}
                aria-label={`${action.label}${action.description ? `. ${action.description}` : ''}`}
                className={[
                  'flex w-full items-center gap-3 rounded-xl border p-4 text-left transition-all',
                  'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-indigo-600',
                  isDisabled
                    ? 'cursor-not-allowed border-slate-200 bg-white opacity-50'
                    : isActive
                      ? 'border-indigo-600 bg-indigo-50 shadow-md ring-1 ring-indigo-600'
                      : 'border-slate-200 bg-white shadow-sm hover:-translate-y-px hover:border-slate-300 hover:shadow active:translate-y-0 active:shadow-sm',
                ].join(' ')}
              >
                <span
                  aria-hidden="true"
                  className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-lg ${
                    isActive && !isDisabled
                      ? 'bg-indigo-600 text-white'
                      : 'bg-slate-100 text-slate-500'
                  }`}
                >
                  <ActionIcon name={action.icon} />
                </span>
                <span className="min-w-0">
                  <span className="block truncate text-sm font-semibold text-slate-900">
                    {action.label}
                  </span>
                  {action.description && (
                    <span className="mt-0.5 block truncate text-xs text-slate-500">
                      {action.description}
                    </span>
                  )}
                </span>
              </button>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
