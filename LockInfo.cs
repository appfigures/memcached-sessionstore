using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

[Serializable]
class LockInfo
{
    public int LockID;
    public DateTime LockTime;

    public LockInfo()
    {
        LockID = new Random().Next();
        LockTime = DateTime.Now;
    }
}