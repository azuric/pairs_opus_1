using SmartQuant.Strategy_;
using SmartQuant;

namespace OpenQuant
{
    class Program
    {
        static void Main(string[] args)
        {
            Scenario_ scenario = new Live(Framework.Current);

            scenario.Run();
        }
    }
}