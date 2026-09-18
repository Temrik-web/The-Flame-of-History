export type IconName = 'document' | 'chart' | 'settings' | 'bell' | 'folder';

export interface SidebarAction {
  id: string;
  label: string;
  description?: string;
  icon: IconName;
  disabled?: boolean;
  /** Демо-обработчик. Замени на свою функцию — выбор/активация обрабатывается в App. */
  onClick?: (id: string) => void;
}

const demoClick = (id: string) => {
  // TODO: подключи своё действие для кнопки
  console.info(`[demo] нажата кнопка: ${id}`);
};

/**
 * Конфиг правой колонки. Чтобы изменить панель:
 * - поменяй label / description / icon;
 * - добавь или удали элемент массива;
 * - задай disabled: true, чтобы выключить кнопку;
 * - поменяй порядок элементов — порядок в массиве = порядок на экране.
 */
export const sidebarActions: SidebarAction[] = [
  {
    id: 'action-1',
    label: 'Кнопка 1',
    description: 'Описание действия',
    icon: 'document',
    onClick: demoClick,
  },
  {
    id: 'action-2',
    label: 'Кнопка 2',
    description: 'Описание действия',
    icon: 'chart',
    onClick: demoClick,
  },
  {
    id: 'action-3',
    label: 'Кнопка 3',
    description: 'Описание действия',
    icon: 'settings',
    onClick: demoClick,
  },
  {
    id: 'action-4',
    label: 'Кнопка 4',
    description: 'Описание действия',
    icon: 'bell',
    onClick: demoClick,
  },
  {
    id: 'action-5',
    label: 'Кнопка 5',
    description: 'Описание действия',
    icon: 'folder',
    disabled: true,
    onClick: demoClick,
  },
];
