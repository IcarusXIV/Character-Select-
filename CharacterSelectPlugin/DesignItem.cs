// CharacterSelectPlugin/DesignItem.cs
using System;

namespace CharacterSelectPlugin
{
    public class DesignItem
    {
        public Guid Id { get; }
        public Guid? ParentFolderId { get; set; }
        public bool IsFolder { get; }
        public DesignFolder? Folder { get; }
        public CharacterDesign? Design { get; }
        public int SortOrder { get; set; }

        // Folder ctor
        public DesignItem(DesignFolder f)
        {
            Id = f.Id;
            IsFolder = true;
            Folder = f;
            ParentFolderId = f.ParentFolderId; // adapt if your folder tracks this differently
            SortOrder = f.SortOrder;      // assume you’ve persisted this
        }

        // Design ctor
        public DesignItem(CharacterDesign d)
        {
            Id = d.Id;
            IsFolder = false;
            Design = d;
            ParentFolderId = d.FolderId;
            SortOrder = d.SortOrder;
        }
    }
}
