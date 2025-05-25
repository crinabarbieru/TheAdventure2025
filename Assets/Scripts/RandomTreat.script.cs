using TheAdventure.Scripting;
using System;
using TheAdventure;

public class RandomTreat : IScript
{
    DateTimeOffset _nextTreatTimestamp;

    public void Initialize()
    {
        _nextTreatTimestamp = DateTimeOffset.UtcNow.AddSeconds(Random.Shared.Next(5, 9));
    }

    public void Execute(Engine engine)
    {
        if (_nextTreatTimestamp < DateTimeOffset.UtcNow)
        {
            _nextTreatTimestamp = DateTimeOffset.UtcNow.AddSeconds(Random.Shared.Next(5, 9));
            var treatPosX = Random.Shared.Next(100, 640);
            var treatPosY = Random.Shared.Next(100, 400);
            engine.AddTreat(treatPosX, treatPosY, false);
        }
    }
}