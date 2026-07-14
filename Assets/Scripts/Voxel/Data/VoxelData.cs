// Fichier: VoxelData.cs (ou similaire)
using System;

// Utiliser un 'ushort' (ou même 'byte' si moins de 256 types de blocs suffisent)
// est plus léger en mémoire qu'un 'int' ou un 'enum' complet (sauf si l'enum a un 'byte' comme type sous-jacent).
// On peut aussi utiliser une struct pour encapsuler plus de données si besoin (lumière, metadata, etc.)

public enum VoxelType : ushort // ushort permet jusqu'à 65535 types de blocs
{
    Air = 0, // Important: Air = 0 pour faciliter les vérifications
    Stone = 1,
    Dirt = 2,
    Grass = 3,
    Wood = 4,
    Leaves = 5,
    Water = 6,
    Lava = 7,
    Sand = 8,
    Snow = 9,
    cobblestone = 10,
    // ... ajoutez autant de types que nécessaire
}

// Option 1: Juste l'ID (très léger)
// Dans le chunk, on aurait un tableau de VoxelType: VoxelType[,,]

// Option 2: Une struct pour plus de flexibilité future (lumière, état, etc.)
[Serializable] // Utile si on veut sauvegarder/sérialiser les chunks
public struct Voxel : IEquatable<Voxel>
{
    public VoxelType type;
    // public byte lightLevel; // Exemple: pour la gestion de la lumière
    // public byte metadata;    // Exemple: orientation d'un escalier, état d'un fourneau

    // Constructeur pour faciliter la création
    public Voxel(VoxelType type)
    {
        this.type = type;
        // this.lightLevel = 0;
        // this.metadata = 0;
    }

    // Propriété pour vérifier rapidement si c'est de l'air (ou un bloc "transparent")
    public bool IsSolid
    {
        get { return type != VoxelType.Air; } // Pourrait être plus complexe (eau, feuilles, etc.)
    }

    // IEquatable pur (pas de reflection) : nécessaire pour VoxelBrick.TryCompact et
    // pour toute utilisation future dans des jobs Burst (Voxel comme clé/valeur de
    // NativeContainer).
    public bool Equals(Voxel other) => type == other.type;
    public override bool Equals(object obj) => obj is Voxel other && Equals(other);
    public override int GetHashCode() => (int)type;
}

// Dans le chunk, on aurait un tableau de Voxel: Voxel[,,] data;