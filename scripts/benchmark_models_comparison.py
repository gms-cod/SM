# -*- coding: utf-8 -*-
"""
Бенчмарк сравнения моделей OpenRouter Free и OpenCode Native Free:
замер времени первого ответа, времени генерации, длины ответа и точности логики.
"""
import os
import sys
import time
import subprocess
import json

if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

BENCHMARK_PROMPT = """Проанализируй фрагмент кода C#:
public class Counter {
    private int _count = 0;
    public void Increment() { _count++; }
    public int Get() => _count;
}
Кратко объясни проблему гонки (Race Condition) и напиши исправленную потокобезопасную версию с Interlocked на русском языке."""

MODELS_TO_TEST = [
    # Штатные бесплатные модели OpenCode
    ("opencode/deepseek-v4.1-flash-free", "OpenCode Native"),
    ("opencode/beam-free", "OpenCode Native"),
    ("opencode/space-bunny-free", "OpenCode Native"),
    # Бесплатные модели OpenRouter
    ("openrouter/nvidia/nemotron-3.5-super-vl:free", "OpenRouter Free"),
    ("openrouter/minimax/minimax-m3.1-flash:free", "OpenRouter Free"),
    ("openrouter/google/gemma-4-31b-it:free", "OpenRouter Free"),
]

def test_model(model_name, category):
    print(f"\nТестирование: [{category}] {model_name}...")
    start_time = time.time()
    try:
        proc = subprocess.Popen(
            ["opencode", "run", "--pure", "-m", model_name],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            encoding="utf-8",
            errors="replace"
        )
        stdout, stderr = proc.communicate(input=BENCHMARK_PROMPT, timeout=60)
        elapsed = time.time() - start_time
        lines = [l for l in stdout.splitlines() if l.strip() and not l.startswith(">")]
        sample = "\n".join(lines[:3]) if lines else "(пустой ответ)"
        char_count = len(stdout)
        
        # Проверка ключевых признаков корректности решения задачи
        has_interlocked = "Interlocked.Increment" in stdout or "Interlocked" in stdout
        has_explanation = "гонк" in stdout.lower() or "поток" in stdout.lower() or "race" in stdout.lower()
        quality_score = "Отлично" if (has_interlocked and has_explanation) else ("Хорошо" if has_interlocked else "Слабо")

        print(f"  Время ответа: {elapsed:.2f} сек")
        print(f"  Символов: {char_count}")
        print(f"  Оценка логики: {quality_score}")
        print(f"  Фрагмент: {sample[:120]}...")
        return {
            "model": model_name,
            "category": category,
            "elapsed_seconds": round(elapsed, 2),
            "char_count": char_count,
            "quality": quality_score,
            "success": True,
            "snippet": sample[:150]
        }
    except subprocess.TimeoutExpired:
        proc.kill()
        print(f"  [!] Тайм-аут (> 60 сек)")
        return {"model": model_name, "category": category, "elapsed_seconds": 60.0, "char_count": 0, "quality": "Timeout", "success": False}
    except Exception as e:
        print(f"  [!] Ошибка: {e}")
        return {"model": model_name, "category": category, "elapsed_seconds": 0, "char_count": 0, "quality": f"Error: {e}", "success": False}

def main():
    print("=" * 70)
    print("СРАВНИТЕЛЬНЫЙ БЕНЧМАРК: OPENCODE NATIVE vs OPENROUTER FREE")
    print("=" * 70)
    results = []
    for model_name, cat in MODELS_TO_TEST:
        res = test_model(model_name, cat)
        results.append(res)

    print("\n" + "=" * 70)
    print("ИТОГОВАЯ СВОДНАЯ ТАБЛИЦА")
    print("=" * 70)
    print(f"{'Категория':<18} | {'Модель':<40} | {'Время (с)':<10} | {'Качество':<10}")
    print("-" * 85)
    for r in results:
        print(f"{r['category']:<18} | {r['model']:<40} | {str(r['elapsed_seconds']) + 's':<10} | {r['quality']:<10}")

    # Сохраняем результат бенчмарка в JSON
    os.makedirs("logs", exist_ok=True)
    with open("logs/model_comparison_benchmark.json", "w", encoding="utf-8") as f:
        json.dump(results, f, ensure_ascii=False, indent=2)
    print(f"\n[+] Результаты сохранены в logs/model_comparison_benchmark.json")

if __name__ == "__main__":
    main()

