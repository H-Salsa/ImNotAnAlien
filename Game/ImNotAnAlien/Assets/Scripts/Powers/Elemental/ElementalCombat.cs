using System.Collections.Generic;
using UnityEngine;

// As REGRAS dos poderes elementais (Missão 21): o que acontece com quem é atingido.
// - Fogo: explode numa área, queima por alguns segundos, empurra objetos soltos, machuca casas e carros.
// - Gelo: deixa lento e congela; o disparo carregado congela o alvo e resfria quem está em volta.
// - Eletricidade: atordoa e pula para os vizinhos; na água pula mais longe e para mais alvos.
// Os números ficam no ElementalTuning. A parte visual é do ElementalFx.
public static class ElementalCombat
{
    private static readonly Collider[] Overlaps = new Collider[96];
    private static readonly HashSet<Object> Seen = new HashSet<Object>();
    private static readonly List<AlienHealth> Chain = new List<AlienHealth>();

    // ---------- Fogo ----------

    public static void FireImpact(Vector3 point, Vector3 normal, Collider direct, float charge, Transform owner)
    {
        ElementalTuning tuning = ElementalTuning.Current;
        float radius = ElementalTuning.Pick(tuning.fireRadius, charge);
        float damage = ElementalTuning.Pick(tuning.fireDamage, charge);
        float dps = ElementalTuning.Pick(tuning.burnDps, charge);
        float seconds = ElementalTuning.Pick(tuning.burnSeconds, charge);
        float push = ElementalTuning.Pick(tuning.firePush, charge);
        float structureDamage = ElementalTuning.Pick(tuning.fireStructureDamage, charge);

        Vector3 center = point + normal * 0.25f;
        ElementalFx.FireBlast(center, radius, charge);
        ElementalFxRunner.Play(ElementalFxRunner.Sound.FireBoom, center, Mathf.Lerp(0.7f, 1f, charge), Mathf.Lerp(1.25f, 0.9f, charge));

        AlienHealth directAlien = direct != null ? direct.GetComponentInParent<AlienHealth>() : null;
        bool attacked = false;
        Seen.Clear();
        int count = Physics.OverlapSphereNonAlloc(center, radius, Overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = Overlaps[i];
            if (hit == null || (owner != null && hit.transform.IsChildOf(owner)))
            {
                continue;
            }

            Vector3 closest = Closest(hit, center);
            float falloff = Mathf.Clamp01(1f - Vector3.Distance(center, closest) / Mathf.Max(0.01f, radius));
            falloff = Mathf.Lerp(0.35f, 1f, falloff);

            AlienHealth alien = hit.GetComponentInParent<AlienHealth>();
            if (alien != null)
            {
                if (!Seen.Add(alien) || alien.IsDead)
                {
                    continue;
                }

                float scale = alien == directAlien ? 1f : falloff;
                ElementalStatus status = ElementalStatus.For(alien);
                if (status != null)
                {
                    status.Burn(dps, seconds * Mathf.Lerp(0.6f, 1f, scale));
                }

                // Carregado: quem está perto do centro é derrubado; os outros cambaleiam.
                if (alien.TryGetComponent(out AlienBrain brain))
                {
                    Vector3 away = alien.transform.position - center;
                    brain.OnStruck(away, Mathf.Lerp(50f, 520f, charge) * scale);
                }

                alien.TakeDamage(damage * scale);
                attacked = true;
                continue;
            }

            DestructibleStructure structure = hit.GetComponentInParent<DestructibleStructure>();
            if (structure != null)
            {
                if (Seen.Add(structure))
                {
                    structure.TakeBlast(structureDamage * falloff, closest, center, radius);
                }

                continue;
            }

            Rigidbody body = hit.attachedRigidbody;
            if (body == null || !Seen.Add(body))
            {
                continue;
            }

            HurtMachine(body.gameObject, damage * falloff * 2f, falloff);
            if (!body.isKinematic)
            {
                body.AddExplosionForce(push, center, radius * 1.3f, 0.5f, ForceMode.Impulse);
            }
        }

        ReportIfHit(center, attacked);
    }

    // ---------- Gelo ----------

    public static void IceImpact(Vector3 point, Vector3 normal, Collider direct, float charge, Transform owner)
    {
        ElementalTuning tuning = ElementalTuning.Current;
        float radius = ElementalTuning.Pick(tuning.iceRadius, charge);
        float damage = ElementalTuning.Pick(tuning.iceDamage, charge);
        float freeze = ElementalTuning.Pick(tuning.freezeSeconds, charge);
        bool charged = charge >= 0.6f;

        Vector3 center = point + normal * 0.2f;
        ElementalFx.IceBurst(center, radius, charge);
        ElementalFxRunner.Play(ElementalFxRunner.Sound.IceHit, center, Mathf.Lerp(0.7f, 1f, charge), Mathf.Lerp(1.15f, 0.85f, charge));
        if (charged && Mathf.Abs(normal.y) > 0.5f)
        {
            ElementalSpikes.Spawn(point, radius, Mathf.RoundToInt(Mathf.Lerp(5f, 11f, charge)), 3.5f);
        }

        AlienHealth directAlien = direct != null ? direct.GetComponentInParent<AlienHealth>() : null;
        Seen.Clear();
        if (directAlien != null && !directAlien.IsDead)
        {
            Seen.Add(directAlien);
            ElementalStatus status = ElementalStatus.For(directAlien);
            if (status != null)
            {
                if (status.IsFrozen)
                {
                    // Acertar de novo um congelado: o gelo estilhaça (dano extra) em vez de só renovar.
                    status.Shatter();
                }
                else if (charged)
                {
                    status.Freeze(freeze);
                }
                else
                {
                    status.Chill(1, freeze);
                }
            }

            directAlien.TakeDamage(damage);
            ReportIfHit(center, true);
        }
        else if (direct != null && direct.attachedRigidbody != null)
        {
            HurtMachine(direct.attachedRigidbody.gameObject, damage * 2f, 1f);
        }

        if (radius < 0.5f)
        {
            return;
        }

        int count = Physics.OverlapSphereNonAlloc(center, radius, Overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = Overlaps[i];
            if (hit == null || (owner != null && hit.transform.IsChildOf(owner)))
            {
                continue;
            }

            AlienHealth alien = hit.GetComponentInParent<AlienHealth>();
            if (alien == null || alien.IsDead || !Seen.Add(alien))
            {
                continue;
            }

            ElementalStatus status = ElementalStatus.For(alien);
            if (status != null)
            {
                status.Chill(charge >= 0.95f ? tuning.freezeStacks : 2, freeze * 0.7f);
            }

            alien.TakeDamage(damage * 0.5f);
        }
    }

    // ---------- Eletricidade ----------

    // Devolve os pontos por onde o raio passou depois do primeiro impacto (para desenhar os pulos).
    public static void LightningImpact(Vector3 point, Vector3 normal, Collider direct, float charge, Transform owner, List<Vector3> jumps)
    {
        ElementalTuning tuning = ElementalTuning.Current;
        float damage = ElementalTuning.Pick(tuning.boltDamage, charge);
        float stun = ElementalTuning.Pick(tuning.stunSeconds, charge);
        float reach = ElementalTuning.Pick(tuning.jumpRadius, charge);
        int maxJumps = Mathf.RoundToInt(Mathf.Lerp(tuning.boltJumps.x, tuning.boltJumps.y, charge));
        jumps.Clear();

        AlienHealth first = direct != null ? direct.GetComponentInParent<AlienHealth>() : null;
        if (first == null || first.IsDead)
        {
            // Não acertou ninguém em cheio: o raio "procura" um alienígena bem perto do ponto de impacto.
            first = Nearest(point, 2.5f, owner, null);
            if (first == null && direct != null && direct.attachedRigidbody != null)
            {
                Rigidbody body = direct.attachedRigidbody;
                HurtMachine(body.gameObject, damage * 2f, 1f);
                if (!body.isKinematic)
                {
                    body.AddForceAtPosition(-normal * Mathf.Lerp(40f, 220f, charge), point, ForceMode.Impulse);
                }
            }
        }

        if (first == null)
        {
            return;
        }

        Chain.Clear();
        Seen.Clear();
        AlienHealth current = first;
        Vector3 from = point;
        for (int jump = 0; jump <= maxJumps && current != null; jump++)
        {
            Chain.Add(current);
            Seen.Add(current);
            Vector3 middle = Center(current);
            if (jump > 0 || (middle - point).sqrMagnitude > 0.5f)
            {
                jumps.Add(from);
                jumps.Add(middle);
            }

            from = middle;

            // Na água a eletricidade vai mais longe e pega mais gente.
            bool wet = IsWet(middle);
            float radius = reach * (wet ? tuning.waterReach : 1f);
            if (wet && jump == 0)
            {
                maxJumps += tuning.waterExtraJumps;
            }

            current = Nearest(middle, radius, owner, Seen);
        }

        float scale = 1f;
        foreach (AlienHealth alien in Chain)
        {
            ElementalStatus status = ElementalStatus.For(alien);
            if (status != null)
            {
                status.Shock(stun * Mathf.Lerp(0.6f, 1f, scale));
            }

            if (alien != first)
            {
                ElementalFx.BoltHit(Center(alien), Vector3.up, charge * 0.4f);
            }

            alien.TakeDamage(damage * scale);
            scale *= tuning.jumpFalloff;
        }

        ReportIfHit(point, Chain.Count > 0);
    }

    // ---------- Ajuda ----------

    // Atacar alienígena na frente de testemunhas é crime (pouco, perto de matar): a polícia reage.
    private static void ReportIfHit(Vector3 position, bool attacked)
    {
        if (attacked)
        {
            WantedSystem.ReportAssault(position);
        }
    }

    // Máquinas e coisas que não usam o AlienHealth (carros, barris, robôs).
    private static void HurtMachine(GameObject target, float damage, float falloff)
    {
        if (target.TryGetComponent(out ExplosiveVehicle vehicle))
        {
            vehicle.TakeDamage(damage);
        }

        if (target.TryGetComponent(out EnemyQuadShell shell))
        {
            shell.TakeDamage(damage * 0.5f);
        }

        if (target.TryGetComponent(out EnemyDrone drone))
        {
            drone.OnBlast(Mathf.Max(0.5f, falloff));
        }
    }

    public static Vector3 Center(AlienHealth alien)
    {
        if (alien.TryGetComponent(out ElementalStatus status))
        {
            return status.WorldCenter;
        }

        return alien.TryGetComponent(out Collider collider) ? collider.bounds.center : alien.transform.position + Vector3.up;
    }

    // Ponto do colisor mais perto de "point". (Malha não convexa só aceita a caixa em volta.)
    private static Vector3 Closest(Collider collider, Vector3 point)
    {
        if (collider is MeshCollider mesh && !mesh.convex)
        {
            return collider.bounds.ClosestPoint(point);
        }

        return collider.ClosestPoint(point);
    }

    // O ponto está dentro d'água? (mar, lago, rio do relevo)
    public static bool IsWet(Vector3 point)
    {
        if (!PlanetTerrain.Active)
        {
            return false;
        }

        float water = WorldOrigin.ToWorld(new Vector3(0f, PlanetTerrain.WaterLevel, 0f)).y;
        return point.y < water + 0.5f && AquaticAlien.WaterDepth(point) > 0.3f;
    }

    private static AlienHealth Nearest(Vector3 point, float radius, Transform owner, HashSet<Object> skip)
    {
        AlienHealth best = null;
        float bestDistance = float.MaxValue;
        int count = Physics.OverlapSphereNonAlloc(point, radius, Overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = Overlaps[i];
            if (hit == null || (owner != null && hit.transform.IsChildOf(owner)))
            {
                continue;
            }

            AlienHealth alien = hit.GetComponentInParent<AlienHealth>();
            if (alien == null || alien.IsDead || (skip != null && skip.Contains(alien)))
            {
                continue;
            }

            float distance = (Closest(hit, point) - point).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = alien;
            }
        }

        return best;
    }
}
