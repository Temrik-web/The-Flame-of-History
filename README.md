# The Flame of History

Unity 2022.3.62f3, URP. Основная сцена: `Assets/Scenes/SampleScene.unity`.

## Запуск и сборка

Установить редактор указанной версии с Windows Build Support, открыть проект и дождаться импорта. Основная сцена включена в Build Settings. Сборка через `Tools → Сборка → Windows x64` создаёт `Builds/Windows/The Flame of History.exe`. Каталог Builds не коммитится.

Командная сборка (редактор с этим проектом должен быть закрыт):

```powershell
& 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe' -batchmode -quit -projectPath "$PWD" -executeMethod ProjectBuild.Windows -logFile "$PWD/Logs/build-windows.log"
```

## Проверки

```powershell
./Tools/Check-Project.ps1
./Tools/Check-Compilation.ps1
```

Для второй проверки нужны созданные Unity `.csproj`, импортированные зависимости в Library и установленный редактор. Используется Roslyn из Unity: отдельно проверяется C# без `UNITY_EDITOR` и редакторский код. Это не проверяет импорт ассетов, сборку шейдеров или запуск `.exe`.

Регрессионные тесты через Test Runner → EditMode → CombatRegressionTests. Они переходят в Play Mode и открывают пустую сцену: сохранить работу перед запуском. Batchmode:

```powershell
& 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe' -batchmode -projectPath "$PWD" -runTests -testPlatform EditMode -testFilter CombatRegressionTests -testResults "$PWD/Logs/combat-tests.xml" -logFile "$PWD/Logs/combat-tests.log"
```

## Документы

- [Единая боевая система и изменения миграции](Docs/COMBAT.md)
- [План оптимизации с критериями проверки](Docs/OPTIMIZATION_PLAN.md)

Известные ограничения: смерть игрока ещё не оформлена в полноценный экран/рестарт; сохранение охватывает содержимое инвентаря, а не весь мир; часть команд диалогов остаётся заглушками. Эти задачи не входят в миграцию боевой системы.
