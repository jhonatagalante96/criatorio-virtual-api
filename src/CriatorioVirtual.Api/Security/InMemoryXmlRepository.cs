using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace CriatorioVirtual.Api;

internal sealed class InMemoryXmlRepository : IXmlRepository
{
    private readonly List<XElement> elements = [];
    private readonly Lock sync = new();

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        lock (sync)
        {
            return elements.Select(element => new XElement(element)).ToArray();
        }
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);

        lock (sync)
        {
            elements.Add(new XElement(element));
        }
    }
}
