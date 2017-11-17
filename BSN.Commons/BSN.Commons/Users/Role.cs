﻿using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.AspNet.Identity.EntityFramework;

namespace Commons.Users
{
	public class Role : IdentityRole<String, UserRole>
	{
		private static readonly List<Role> Roles = new List<Role>();


		public Role() { }

		private Role(Roles role)
		{
			Name = role.ToString();
		}

		public static Role Of(Roles roles)
		{
			foreach (var role in Roles.Where(role => role.Name.Equals(roles.ToString())))
				return role;
			var newRole = new Role(roles);
			Roles.Add(newRole);
			return newRole;
		}
	}
}
