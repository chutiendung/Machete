﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Machete.Domain.Resources;

namespace Machete.Domain
{
    public class WorkerSkill
    {
        public int ID { get; set; }
        public DateTime datecreated { get; set; }
        public DateTime dateupdated { get; set; }
        public Guid Createdby { get; set; }
        public Guid Updatedby { get; set; }
    }
}