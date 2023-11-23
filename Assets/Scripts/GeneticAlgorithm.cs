using UnityEngine;
using System.Collections.Generic;

namespace IA.GeneAlgo
{
    [System.Serializable]
    public class Genome
    {
        public float[] genome;
        public float fitness = 0;

        public Genome(float[] genes)
        {
            this.genome = genes;
            fitness = 0;
        }

        public Genome(int genesCount)
        {
            genome = new float[genesCount];

            for (int j = 0; j < genesCount; j++)
                genome[j] = Random.Range(-1.0f, 1.0f);

            fitness = 0;
        }

        public Genome()
        {
            fitness = 0;
        }

        public Genome(Genome other)
        {
            fitness = other.fitness;
            genome = other.genome;
        }
    }

    public class GeneticAlgorithm
    {
        List<Genome> population = new List<Genome>();
        List<Genome> newPopulation = new List<Genome>();

        float totalFitness;

        int maxPopulation = 0;
        float mutationChance = 0.0f;
        float mutationRate = 0.0f;

        public GeneticAlgorithm(int maxPopulation, float mutationChance, float mutationRate)
        {
            this.maxPopulation = maxPopulation;
            this.mutationChance = mutationChance;
            this.mutationRate = mutationRate;
        }

        public Genome[] GetRandomGenomes(int count, int genesCount)
        {
            Genome[] genomes = new Genome[count];

            for (int i = 0; i < count; i++)
            {
                genomes[i] = new Genome(genesCount);
            }

            return genomes;
        }
        
        public Genome[] GetRandomPopulation()
        {
            List<Genome> newPopulation = new List<Genome>();
            
            while (newPopulation.Count < population.Count)
            {
                NoFitnessCrossover(newPopulation);
            }

            return newPopulation.ToArray();
        }

        public Genome[] Epoch(Genome[] oldGenomes, bool sort = true)
        {
            totalFitness = 0;

            population.Clear();
            newPopulation.Clear();

            population.AddRange(oldGenomes);
            if(sort)
                population.Sort(HandleComparison);

            foreach (Genome g in population)
            {
                totalFitness += g.fitness;
            }

            while (newPopulation.Count < population.Count)
            {
                mutationChance *= 2;
                Crossover();
                mutationChance /= 2;
            }

            return newPopulation.ToArray();
        }

        public Genome[] Epoch(Genome[] oldGenomes, int eliteCount)
        {
            totalFitness = 0;

            population.Clear();
            newPopulation.Clear();

            population.AddRange(oldGenomes);
            population.Sort(HandleComparison);

            foreach (Genome g in population)
            {
                totalFitness += g.fitness;
            }

            SelectElite(eliteCount);

            while (newPopulation.Count < population.Count)
            {
                Crossover();
            }

            return newPopulation.ToArray();
        }
        
        void SelectElite(int eliteCount)
        {
            for (int i = 0; i < eliteCount && newPopulation.Count < population.Count; i++)
            {
                newPopulation.Add(population[i]);
            }
        }
        
        public Genome[] Epoch(Genome[] elites, Genome[] reproductiveGenomes)
        {
            totalFitness = 0;

            population.Clear();
            newPopulation.Clear();

            population.AddRange(reproductiveGenomes);
            population.Sort(HandleComparison);

            foreach (Genome g in population)
            {
                totalFitness += g.fitness;
            }

            //SelectElite();
            newPopulation.AddRange(elites);
            
            int newPopSize = population.Count + elites.Length;
            
            if(newPopSize > maxPopulation)  newPopSize = maxPopulation;
            
            while (newPopulation.Count < newPopSize)
            {
                Crossover();
            }

            population = newPopulation;
            return newPopulation.ToArray();
        }

        void NoFitnessCrossover(List<Genome> newPopulation)
        {
            Genome mom = population[Random.Range(0, population.Count)];
            Genome dad = population[Random.Range(0, population.Count)];

            Genome child1;
            Genome child2;

            Crossover(mom, dad, out child1, out child2, 2.0f);

            newPopulation.Add(child1);
            newPopulation.Add(child2);
        }
        
        void Crossover()
        {
            Genome mom = RouletteSelection();
            Genome dad = RouletteSelection();

            Genome child1;
            Genome child2;

            Crossover(mom, dad, out child1, out child2);

            newPopulation.Add(child1);
            newPopulation.Add(child2);
        }

        void Crossover(Genome mom, Genome dad, out Genome child1, out Genome child2, float mutMod = 1.0f)
        {
            float mutationRate = mutMod * this.mutationRate;
            
            child1 = new Genome();
            child2 = new Genome();

            child1.genome = new float[mom.genome.Length];
            child2.genome = new float[mom.genome.Length];

            int pivot = Random.Range(0, mom.genome.Length);

            for (int i = 0; i < pivot; i++)
            {
                child1.genome[i] = mom.genome[i];

                if (ShouldMutate(mutMod))
                    child1.genome[i] += Random.Range(-mutationRate, mutationRate);

                child2.genome[i] = dad.genome[i];

                if (ShouldMutate(mutMod))
                    child2.genome[i] += Random.Range(-mutationRate, mutationRate);
            }

            for (int i = pivot; i < mom.genome.Length; i++)
            {
                child2.genome[i] = mom.genome[i];

                if (ShouldMutate())
                    child2.genome[i] += Random.Range(-mutationRate, mutationRate);

                child1.genome[i] = dad.genome[i];

                if (ShouldMutate())
                    child1.genome[i] += Random.Range(-mutationRate, mutationRate);
            }
        }

        bool ShouldMutate(float mutMod = 1.0f)
        {
            return Random.Range(0.0f, 1.0f) < mutationChance * mutMod;
        }

        public static int HandleComparison(Genome x, Genome y)
        {
            return x.fitness > y.fitness ? 1 : x.fitness < y.fitness ? -1 : 0;
        }

        public Genome RouletteSelection()
        {
            float rnd = Random.Range(0, Mathf.Max(totalFitness, 0));

            float fitness = 0;

            for (int i = 0; i < population.Count; i++)
            {
                fitness += Mathf.Max(population[i].fitness, 0);
                if (fitness >= rnd)
                    return population[i];
            }

            return null;
        }
    }
}