import click
from delpyc import find_pycache_dirs, delete_dirs


@click.command()
@click.option(
    "--path",
    "-p",
    default=".",
    help="Root path to search for __pycache__ directories",
)
@click.option(
    "--yes",
    "-y",
    is_flag=True,
    help="Skip confirmation prompt and delete directly",
)
def main(path, yes):
    """Find and delete __pycache__ directories."""
    pycache_dirs = find_pycache_dirs(path)

    if not pycache_dirs:
        click.echo("No __pycache__ directories found.")
        return

    click.echo(f"Found {len(pycache_dirs)} __pycache__ directory(ies):\n")

    for dir_path in pycache_dirs:
        click.echo(f"  {dir_path}")

    click.echo("")

    if yes:
        should_delete = True
    else:
        should_delete = click.confirm("Do you want to delete these directories?")

    if should_delete:
        deleted_count = delete_dirs(pycache_dirs)
        click.echo(f"\nSuccessfully deleted {deleted_count} directory(ies).")
    else:
        click.echo("\nOperation cancelled.")


if __name__ == "__main__":
    main()