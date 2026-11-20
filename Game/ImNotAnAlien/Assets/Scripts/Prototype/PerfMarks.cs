using System.Collections.Generic;
using System.Diagnostics;

// Marcadores do medidor de FPS (20/11).
// Partes do jogo que podem causar engasgo avisam o medidor do que fizeram e quanto tempo levaram.
// Na medição (F4), cada quadro lento sai no relatório com a lista do que aconteceu nele, por exemplo:
//   "51 ms · lixo 290 KB · Bloco: montar 12,3 ms · NavMesh: juntar formas 4,1 ms · Coleta de lixo"
//
// Uso:
//   using (PerfMarks.Time("Bloco: montar")) { ... }   // mede o tempo do trecho
//   PerfMarks.Event("Casa: desabou");                  // só avisa que aconteceu
//
// Fora da medição não faz nada (não mede, não guarda, não gera lixo).
public static class PerfMarks
{
    // Ligado pelo medidor só enquanto grava.
    public static bool Active;

    public struct Mark
    {
        public string tag;
        public float ms;       // tempo total no quadro (0 = só aconteceu)
        public int count;      // quantas vezes no quadro
        public bool timed;
        public bool topLevel;  // não estava dentro de outro marcador (entra na conta do "explicado")
    }

    private const int MaxPerFrame = 32;
    private static readonly Mark[] frame = new Mark[MaxPerFrame];
    private static int frameCount;
    private static int depth;

    public struct Scope : System.IDisposable
    {
        private readonly string tag;
        private readonly long start;
        private readonly bool top;

        internal Scope(string tag)
        {
            this.tag = tag;
            start = Stopwatch.GetTimestamp();
            top = depth == 0;
            depth++;
        }

        public void Dispose()
        {
            if (tag == null)
            {
                return;
            }

            depth = System.Math.Max(0, depth - 1);
            float ms = (float)((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            Add(tag, ms, true, top);
        }
    }

    public static Scope Time(string tag)
    {
        return Active ? new Scope(tag) : default;
    }

    public static void Event(string tag)
    {
        if (Active)
        {
            Add(tag, 0f, false, false);
        }
    }

    private static void Add(string tag, float ms, bool timed, bool top)
    {
        for (int i = 0; i < frameCount; i++)
        {
            if (ReferenceEquals(frame[i].tag, tag) || frame[i].tag == tag)
            {
                frame[i].ms += ms;
                frame[i].count++;
                frame[i].timed |= timed;
                frame[i].topLevel |= top;
                return;
            }
        }

        if (frameCount < MaxPerFrame)
        {
            frame[frameCount++] = new Mark { tag = tag, ms = ms, count = 1, timed = timed, topLevel = top };
        }
    }

    // O medidor pega o que aconteceu no quadro que acabou e zera para o próximo.
    internal static void TakeFrame(List<Mark> into)
    {
        into.Clear();
        for (int i = 0; i < frameCount; i++)
        {
            into.Add(frame[i]);
        }

        frameCount = 0;
        depth = 0;
    }

    internal static void Clear()
    {
        frameCount = 0;
        depth = 0;
    }
}
