/// Helper codeunit exercising ModuleDependencyInfo — issue #759.
codeunit 50134 "MDI Src"
{
    procedure GetDependencyId(Dep: ModuleDependencyInfo): Text
    begin
        exit(Dep.Id());
    end;

    procedure GetDependencyName(Dep: ModuleDependencyInfo): Text
    begin
        exit(Dep.Name());
    end;

    procedure GetDependencyPublisher(Dep: ModuleDependencyInfo): Text
    begin
        exit(Dep.Publisher());
    end;
}
