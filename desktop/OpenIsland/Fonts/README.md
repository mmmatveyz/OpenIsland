# Шрифты

Остров рисуется свободными шрифтами: при сборке все `*.otf` и `*.ttf` из этой папки встраиваются в exe, поэтому вид не зависит от шрифтов, установленных в системе.

| Файл | Что это | Лицензия |
| :--- | :--- | :--- |
| `Inter-Regular.otf`, `Inter-Medium.otf`, `Inter-SemiBold.otf` | Текст интерфейса и цифры, начертания Regular/Medium/SemiBold | [SIL Open Font License 1.1](https://github.com/rsms/inter/blob/master/LICENSE.txt) |
| `Phosphor-Fill.ttf` | Иконки острова (глифы Phosphor Fill) | [MIT](https://github.com/phosphor-icons/web/blob/master/LICENSE) |

Текст берётся из семейства **Inter** (`MainWindow.xaml`), иконки — из шрифта **Phosphor-Fill** (`Icon.cs`). Кодовые точки глифов Phosphor лежат в приватной области `U+E000`–`U+F8FF`; имена и коды перечислены в `Icon.cs` (словарь `Symbols`). Волны громкости и wifi рисуются собственными контурами прямо в коде (`Icon.cs`, словарь `Custom`) ради пошаговой анимации и независимости от конкретного шрифта-иконника.

Оба шрифта распространяются по свободным лицензиям (OFL и MIT) и совместимы с лицензией репозитория.

Если убрать файлы отсюда, текст будет рисоваться `Segoe UI Variable` (или `Segoe UI`), а иконок не будет.
