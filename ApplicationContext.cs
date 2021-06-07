
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Linq.Expressions;

namespace FaceLogin
{
    
    public class ApplicationContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite(@"Data Source=UserFaces.db3");
        public DbSet<Face> Faces { get; set; }
    }

    //Класс сохраненных пользователей
    public class Face
    {
        [Key]
        public int Id { get; set; }
        public Byte[] PhotoUser { get; set; }
        public string NameUser { get; set; }


    }
}
