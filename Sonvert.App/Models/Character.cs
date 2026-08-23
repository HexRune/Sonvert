using System;
using System.Collections.Generic;

namespace Sonvert.App.Models;

public class Character
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<CharacterEmotionClip> EmotionClips { get; set; } = new();
}