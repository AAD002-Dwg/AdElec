using System.Collections.Generic;
using AdElec.Core.Models;

namespace AdElec.Core.Interfaces;

public interface IPanelRepository
{
    // Saves a single panel to the DWG active database
    void SavePanel(Panel panel);

    // Retrieves all saved panels from the drawing
    List<Panel> GetAllPanels();

    // Deletes a panel by name
    void DeletePanel(string panelName);
}
