import { useState } from 'react';
import { ContentBlock } from './components/ContentBlock';
import { MainPanel } from './components/MainPanel';
import { SidebarActions } from './components/SidebarActions';
import { sidebarActions, type SidebarAction } from './config/actions';

export default function App() {
  const [activeId, setActiveId] = useState<string | null>('action-1');
  const [userName, setUserName] = useState('');
  const [statusMessage, setStatusMessage] = useState('Выбери действие справа или нажми главную кнопку.');
  const [isPublished, setIsPublished] = useState(false);

  const activeAction = sidebarActions.find((action) => action.id === activeId) ?? null;

  const handleSelect = (action: SidebarAction) => {
    setActiveId(action.id);
    action.onClick?.(action.id);
    setStatusMessage(`Выбрано: ${action.label}.`);
  };

  const handleMainAction = () => {
    // TODO: подключи сюда своё главное действие (сохранение, отправка формы и т.п.)
    setIsPublished(true);
    setStatusMessage(
      `Главное действие выполнено${userName ? ` для «${userName}»` : ''}.`,
    );
  };

  const handleCheckStatus = () => {
    // TODO: подключи сюда проверку статуса
    setStatusMessage(`Статус проверен в ${new Date().toLocaleTimeString('ru-RU')}.`);
  };

  return (
    <div className="min-h-screen bg-slate-100">
      <main className="mx-auto w-full max-w-6xl px-4 py-6 sm:px-6 sm:py-10">
        <div className="grid grid-cols-1 items-start gap-6 md:grid-cols-[minmax(0,1fr)_240px] lg:grid-cols-[minmax(0,1fr)_300px]">
          <MainPanel
            title="Название проекта"
            subtitle="Краткое описание панели и того, что здесь происходит."
            mainActionLabel="Главное действие"
            onMainAction={handleMainAction}
          >
            <ContentBlock
              title="Блок с полем ввода"
              description="Введи значение — оно подставится в результат главного действия."
              input={{
                id: 'user-name',
                label: 'Название',
                placeholder: 'Например: мой проект',
                value: userName,
                onChange: setUserName,
              }}
            />

            <ContentBlock
              title="Блок со статусом"
              description="Статус меняется после нажатия главной кнопки."
              status={
                isPublished
                  ? { label: 'Готово', tone: 'success' }
                  : { label: 'Ожидает', tone: 'neutral' }
              }
              action={{ label: 'Проверить статус', onClick: handleCheckStatus }}
            />

            <ContentBlock
              title="Информационный блок"
              description="Обычный текстовый блок без ввода. Кнопка ниже выключена для примера состояния disabled."
              action={{ label: 'Недоступно', onClick: () => {}, disabled: true }}
            />

            <p aria-live="polite" className="text-sm text-slate-500">
              {activeAction ? `Активно: ${activeAction.label}. ` : ''}
              {statusMessage}
            </p>
          </MainPanel>

          <SidebarActions
            title="Действия"
            actions={sidebarActions}
            activeId={activeId}
            onSelect={handleSelect}
          />
        </div>
      </main>
    </div>
  );
}
