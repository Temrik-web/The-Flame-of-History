export type IconName = 'document' | 'chart' | 'settings' | 'bell' | 'folder';

export interface SidebarAction {
  id: string;
  label: string;
  description: string;
  detail: string;
  icon: IconName;
  disabled?: boolean;
}

export const sidebarActions: SidebarAction[] = [
  {
    id: 'overview',
    label: 'Обзор',
    description: 'Сцены и технология',
    detail: 'Историческая игра на Unity 2022.3.62f3 и URP. В сборку входят сцены Intro, Main menu и Game. Игровая логика, ассеты и настройки находятся в Unity-проекте рядом с этой страницей.',
    icon: 'document',
  },
  {
    id: 'gameplay',
    label: 'Игровые системы',
    description: 'Бой, диалоги, инвентарь',
    detail: 'В проекте есть единая система здоровья и урона, ИИ противников, оружие, инвентарь, диалоги и квесты. Описание боевой системы находится в Docs/COMBAT.md.',
    icon: 'chart',
  },
  {
    id: 'saves',
    label: 'Сохранения',
    description: 'Новая игра и продолжение',
    detail: 'Новая игра очищает игровой прогресс, сохраняя громкость музыки. Продолжение загружает сохранённый инвентарь и состояние диалогов и квестов. Положение игрока и изменения мира пока не сохраняются.',
    icon: 'folder',
  },
  {
    id: 'optimization',
    label: 'Оптимизация',
    description: 'План измерений',
    detail: 'План профилирования рендеринга, боя, ИИ и памяти находится в Docs/OPTIMIZATION_PLAN.md. Он задаёт сценарий и критерии замеров; подтверждённых показателей ускорения пока нет.',
    icon: 'settings',
  },
];
