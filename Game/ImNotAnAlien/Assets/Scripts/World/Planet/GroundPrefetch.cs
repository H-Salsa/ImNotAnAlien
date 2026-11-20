using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

// Chão dos blocos calculado em segundo plano (20/11).
// O medidor mostrou que o chão (alturas do relevo + cores da textura) era a parte mais pesada de montar um
// bloco: 9 a 40 ms num quadro só. Agora o WorldStreamer pede o chão dos blocos que estão PERTO de entrar,
// e as contas rodam em outros núcleos do processador (o jogo usava praticamente um só). Quando o bloco
// entra, o PlanetTerrain.BuildGround só pega o resultado e monta a malha e a textura (leve).
// Se o cálculo ainda não terminou, o BuildGround calcula na hora, como antes (nunca fica sem chão).
//
// Só matemática: a thread de fora não mexe em nada do Unity (nem cria malha, nem textura).
public static class GroundPrefetch
{
    private sealed class Job
    {
        public int mode;
        public int generation;
        public Task<PlanetTerrain.GroundData> task;
    }

    private static readonly ConcurrentDictionary<Vector2Int, Job> jobs = new ConcurrentDictionary<Vector2Int, Job>();
    private static volatile int generation;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Clear();
    }

    public static int Pending => jobs.Count;

    // Pede o chão de um bloco (não faz nada se já foi pedido com o mesmo modo).
    public static void Request(Vector2Int coord, int mode)
    {
        if (jobs.TryGetValue(coord, out Job existing) && existing.mode == mode && existing.generation == generation)
        {
            return;
        }

        int gen = generation;
        Job job = new Job { mode = mode, generation = gen };
        job.task = Task.Run(() =>
        {
            try
            {
                return gen == generation ? PlanetTerrain.ComputeGround(coord, mode) : null;
            }
            catch (System.Exception)
            {
                return null; // planeta trocado no meio da conta, por exemplo: o BuildGround calcula na hora
            }
        });
        jobs[coord] = job;
    }

    // Pega o chão pronto (e tira da lista). false = não foi pedido, ainda não terminou ou é de outro modo.
    public static bool TryTake(Vector2Int coord, int mode, out PlanetTerrain.GroundData data)
    {
        data = null;
        if (!jobs.TryGetValue(coord, out Job job))
        {
            return false;
        }

        if (job.mode != mode || job.generation != generation)
        {
            jobs.TryRemove(coord, out _);
            return false;
        }

        jobs.TryRemove(coord, out _);
        if (!job.task.IsCompleted)
        {
            // Ainda calculando: espera terminar (já começou, então é mais rápido que calcular do zero).
            using (PerfMarks.Time("Chão > esperar o cálculo de fundo"))
            {
                try
                {
                    job.task.Wait();
                }
                catch (System.Exception)
                {
                    return false;
                }
            }
        }

        data = job.task.Status == TaskStatus.RanToCompletion ? job.task.Result : null;
        return data != null;
    }

    // Esquece os pedidos de blocos que ficaram longe (para não gastar memória).
    private static readonly List<Vector2Int> scratch = new List<Vector2Int>();

    public static void Prune(System.Func<Vector2Int, bool> keep)
    {
        scratch.Clear();
        foreach (KeyValuePair<Vector2Int, Job> pair in jobs)
        {
            if (!keep(pair.Key))
            {
                scratch.Add(pair.Key);
            }
        }

        foreach (Vector2Int coord in scratch)
        {
            jobs.TryRemove(coord, out _);
        }
    }

    public static void Clear()
    {
        generation++;
        jobs.Clear();
    }
}
