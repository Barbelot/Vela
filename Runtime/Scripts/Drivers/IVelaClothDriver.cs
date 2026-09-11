namespace Vela.Drivers
{
    /// <summary>Advances a cloth by wall-clock time. The solver itself is time-agnostic; drivers own the clock.</summary>
    public interface IVelaClothDriver
    {
        void Tick(float deltaTime);
        void Rewind();
    }
}
